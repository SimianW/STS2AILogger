#!/usr/bin/env bash
set -euo pipefail

if [[ $# -ne 1 ]]; then
  echo "usage: tools/validate-log.sh <run-log.jsonl>" >&2
  exit 2
fi

file="$1"
if [[ ! -f "$file" ]]; then
  echo "missing file: $file" >&2
  exit 2
fi

failures=0

check_count() {
  local label="$1"
  local count="$2"
  if [[ "$count" != "0" ]]; then
    echo "FAIL $label: $count"
    failures=$((failures + 1))
  else
    echo "PASS $label"
  fi
}

event_count="$(jq -s 'length' "$file")"
echo "events: $event_count"

check_count "event_index is contiguous" \
  "$(jq -s 'to_entries | map(select(.value.event_index != .key)) | length' "$file")"

check_count "schema_version is 3" \
  "$(jq -s 'map(select(.schema_version != 3)) | length' "$file")"

check_count "session_id exists" \
  "$(jq -s 'map(select(.session_id == null)) | length' "$file")"

check_count "run_id exists" \
  "$(jq -s 'map(select(.run_id == null)) | length' "$file")"

check_count "run_key exists when run_id exists" \
  "$(jq -s 'map(select(.run_id != null and .run_key == null)) | length' "$file")"

check_count "timestamps are monotonic" \
  "$(jq -s '[range(1; length) as $i | select(.[$i].timestamp_utc < .[$i - 1].timestamp_utc)] | length' "$file")"

check_count "card play started/finished pairs match" \
  "$(jq -s '
    [.[] | select(.type == "card_play_started" or .type == "card_play_finished")] as $plays |
    if ($plays | length) == 0 then
      0
    elif all($plays[]; (.payload.card_play.correlation_id // null) != null) then
      [$plays[] | {
        id: .payload.card_play.correlation_id,
        type: .type
      }] |
      group_by(.id) |
      map(select(
        (map(select(.type == "card_play_started")) | length) != 1 or
        (map(select(.type == "card_play_finished")) | length) != 1
      )) |
      length
    else
      [$plays[] | {
        card: .payload.card_play.card.id,
        type: .type
      }] |
      group_by(.card) |
      map(select(
        (map(select(.type == "card_play_started")) | length) !=
        (map(select(.type == "card_play_finished")) | length)
      )) |
      length
    end
  ' "$file")"

check_count "card selections offered/selected pairs match" \
  "$(jq -s '
    [.[] | select(.type == "card_selection_offered" or .type == "card_selection_selected") |
      {id: .payload.selection_id, type: .type}] |
    group_by(.id) |
    map(select((map(select(.type == "card_selection_offered")) | length) !=
               (map(select(.type == "card_selection_selected")) | length))) |
    length
  ' "$file")"

check_count "completed combats have terminal event" \
  "$(jq -s '
    . as $events |
    def invalidated($idx):
      any($events[]; .type == "state_rewind_detected" and
        (.payload.invalidated_from_event_index // -1) <= $idx and
        $idx <= (.payload.invalidated_to_event_index // -1));

    ([$events[] | select((invalidated(.event_index) | not))]) as $valid |
    [range(0; ($valid | length)) as $i |
      select($valid[$i].type == "combat_setup") |
      ([$valid[($i + 1):][] | select(.type == "combat_setup" or .type == "combat_ended" or .type == "run_ended")][0]) as $next |
      select($next != null and $next.type != "combat_ended" and $next.type != "run_ended")
    ] |
    length
  ' "$file")"

check_count "rerun files are not tiny late-event fragments" \
  "$(jq -s '
    if length == 0 then
      0
    elif ((.[0].run_id // "") | contains("-rerun-")) and length <= 2 and any(.[]; .type != "run_log_attached") then
      1
    else
      0
    end
  ' "$file")"

check_count "deck mutations are not duplicated" \
  "$(jq -s '
    [.[] | select(.type == "deck_card_added" or .type == "deck_card_removed") |
      {key: ([.type, (.payload.player.net_id | tostring), .payload.card.id, (.payload.deck | tojson)] | join("|"))}] |
    group_by(.key) |
    map(select(length > 1)) |
    length
  ' "$file")"

check_count "run_loaded rewinds are marked" \
  "$(jq -s '
    def player_state($event):
      ($event.payload.local_player //
       $event.payload.player //
       $event.payload.run.players[0]? //
       $event.payload.combat.players[0]?) as $player |
      if $player == null then
        null
      else
        {
          act_index: ($event.payload.run.act_index //
                      $event.payload.combat.run.act_index //
                      null),
          total_floor: ($event.payload.run.total_floor //
                        $event.payload.combat.run.total_floor //
                        null),
          room_type: ($event.payload.run.room.type //
                      $event.payload.room.type //
                      $event.payload.combat.run.room.type //
                      null),
          room_model: ($event.payload.run.room.model //
                       $event.payload.room.model //
                       $event.payload.combat.run.room.model //
                       null),
          in_combat: $player.in_combat,
          combat_encounter: ($event.payload.combat.encounter // null),
          combat_round: ($event.payload.combat.round // null),
          hp: $player.hp,
          max_hp: $player.max_hp,
          gold: $player.gold,
          deck: [($player.deck // [])[] | .id],
          relics: ($player.relics // []),
          potions: ($player.potions // [])
        }
      end;

    [range(0; length) as $i |
      select(.[$i].type == "run_loaded") |
      (player_state(.[$i])) as $loaded |
      ([.[:$i][] | player_state(.) as $state | select($state != null) | $state] | last) as $previous |
      select($previous != null and $loaded != null and $previous != $loaded and (.[$i + 1].type? != "state_rewind_detected"))
    ] |
    length
  ' "$file")"

check_count "map node selections resolve" \
  "$(jq -s '
    [.[] | select(.type == "map_nodes_offered" or .type == "map_node_selected" or .type == "map_node_resolved") |
      {id: .payload.selection_id, type: .type}] |
    group_by(.id) |
    map(select(length > 0 and (
      (map(select(.type == "map_nodes_offered")) | length) != 1 or
      (map(select(.type == "map_node_selected")) | length) != 1 or
      (map(select(.type == "map_node_resolved")) | length) != 1
    ))) |
    length
  ' "$file")"

check_count "treasure relic selections terminate" \
  "$(jq -s '
    [.[] | select(.type == "treasure_relics_offered" or .type == "treasure_relic_selected" or .type == "treasure_relic_skipped") |
      {
        id: .payload.selection_id,
        type: .type,
        result: (.payload.selection_result // null),
        candidate_count: ((.payload.candidates // []) | length)
      }
    ] |
    group_by(.id) |
    map(select(length > 0 and (
      (map(select(.type == "treasure_relics_offered")) | length) as $offers |
      (map(select(.type == "treasure_relic_selected" or .type == "treasure_relic_skipped"))) as $terminals |
      (($offers == 1 and ($terminals | length) == 1) or
       ($offers == 0 and
        ($terminals | length) == 1 and
        ($terminals | all(.result == "no_candidates" and .candidate_count == 0))))
      | not
    ))) |
    length
  ' "$file")"

if [[ "$failures" -gt 0 ]]; then
  exit 1
fi
