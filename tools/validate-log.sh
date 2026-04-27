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

check_count "schema_version is 2" \
  "$(jq -s 'map(select(.schema_version != 2)) | length' "$file")"

check_count "session_id exists" \
  "$(jq -s 'map(select(.session_id == null)) | length' "$file")"

check_count "run_id exists" \
  "$(jq -s 'map(select(.run_id == null)) | length' "$file")"

check_count "timestamps are monotonic" \
  "$(jq -s '[range(1; length) as $i | select(.[$i].timestamp_utc < .[$i - 1].timestamp_utc)] | length' "$file")"

check_count "card play started/finished pairs match" \
  "$(jq -s '
    [.[] | select(.type == "card_play_started" or .type == "card_play_finished") |
      {i: .event_index, t: .type, c: .payload.card_play.card.id}] as $plays |
    [range(0; ($plays | length); 2) as $i |
      select($plays[$i].t != "card_play_started" or
             $plays[$i + 1].t != "card_play_finished" or
             $plays[$i].c != $plays[$i + 1].c)] |
    length
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

if [[ "$failures" -gt 0 ]]; then
  exit 1
fi
