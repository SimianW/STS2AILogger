using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using MegaCrit.Sts2.Core.CardSelection;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Potions;
using MegaCrit.Sts2.Core.Runs;

namespace STS2AILogger.STS2AILoggerCode.Logging;

public sealed record CardSelectionLogState(
    string SelectionId,
    string SelectionKind,
    Player? Player,
    IReadOnlyList<CardModel> Candidates,
    IReadOnlyList<object> OfferedCandidates,
    CardSelectorPrefs? Prefs,
    bool CanSkip,
    bool RequiresConfirmation,
    AbstractModel? Source);

public static class SelectionSnapshots
{
    private static long _nextSelectionId;

    public static string NextSelectionId()
    {
        return $"choice-{Interlocked.Increment(ref _nextSelectionId)}";
    }

    public static CardSelectionLogState Create(
        string selectionKind,
        Player? player,
        IEnumerable<CardModel>? candidates,
        CardSelectorPrefs? prefs,
        bool canSkip = false,
        bool? requiresConfirmation = null,
        AbstractModel? source = null)
    {
        List<CardModel> candidateList = candidates?.ToList() ?? new List<CardModel>();
        return new CardSelectionLogState(
            NextSelectionId(),
            selectionKind,
            player,
            candidateList,
            CandidateSnapshots(candidateList),
            prefs,
            canSkip,
            requiresConfirmation ?? prefs?.RequireManualConfirmation ?? false,
            source);
    }

    public static object Offered(CardSelectionLogState state)
    {
        RunState? runState = state.Player?.RunState as RunState;
        CombatState? combatState = state.Player?.Creature.CombatState;
        return new
        {
            selection_id = state.SelectionId,
            selection_kind = state.SelectionKind,
            source = Source(state.Source),
            player = GameSnapshots.Player(state.Player),
            run = GameSnapshots.Run(runState),
            combat = GameSnapshots.Combat(combatState),
            prompt = Prompt(state),
            min_select = MinSelect(state),
            max_select = MaxSelect(state),
            can_skip = EffectiveCanSkip(state),
            can_skip_requested = state.CanSkip,
            requires_confirmation = state.RequiresConfirmation,
            candidates = CandidateSnapshots(state)
        };
    }

    public static object Selected(CardSelectionLogState state, object selectedCards)
    {
        IReadOnlyList<CardModel> selected = ToCardList(selectedCards);
        RunState? runState = state.Player?.RunState as RunState;
        CombatState? combatState = state.Player?.Creature.CombatState;
        return new
        {
            selection_id = state.SelectionId,
            selection_kind = state.SelectionKind,
            source = Source(state.Source),
            prompt = Prompt(state),
            min_select = MinSelect(state),
            max_select = MaxSelect(state),
            can_skip = EffectiveCanSkip(state),
            can_skip_requested = state.CanSkip,
            requires_confirmation = state.RequiresConfirmation,
            selection_result = SelectionResult(state, selected),
            candidates = CandidateSnapshots(state),
            selected = selected.Select(card => new
            {
                index = IndexOfReference(state.Candidates, card),
                card = GameSnapshots.Card(card)
            }).ToList(),
            skipped = selected.Count == 0 && state.Candidates.Count > 0,
            player = GameSnapshots.Player(state.Player),
            run = GameSnapshots.Run(runState),
            combat = GameSnapshots.Combat(combatState)
        };
    }

    private static List<object> CandidateSnapshots(CardSelectionLogState state)
    {
        return state.OfferedCandidates.ToList();
    }

    private static List<object> CandidateSnapshots(IReadOnlyList<CardModel> candidates)
    {
        return candidates.Select((card, index) => new
        {
            index,
            card = GameSnapshots.Card(card)
        }).Cast<object>().ToList();
    }

    private static int? MinSelect(CardSelectionLogState state)
    {
        if (state.Prefs.HasValue)
        {
            return state.Prefs.Value.MinSelect;
        }

        return state.SelectionKind == "card_reward" ? 0 : null;
    }

    private static int? MaxSelect(CardSelectionLogState state)
    {
        if (state.Prefs.HasValue)
        {
            return state.Prefs.Value.MaxSelect;
        }

        return state.SelectionKind == "card_reward" ? 1 : null;
    }

    private static bool EffectiveCanSkip(CardSelectionLogState state)
    {
        return state.CanSkip || state.Candidates.Count == 0;
    }

    private static string SelectionResult(CardSelectionLogState state, IReadOnlyList<CardModel> selected)
    {
        if (selected.Count > 0)
        {
            return "selected";
        }

        return state.Candidates.Count == 0 ? "no_candidates" : "skipped";
    }

    private static IReadOnlyList<CardModel> ToCardList(object selectedCards)
    {
        return selectedCards switch
        {
            CardModel card => new List<CardModel> { card },
            IEnumerable<CardModel> cards => cards.Where(card => card != null).ToList(),
            _ => new List<CardModel>()
        };
    }

    private static int IndexOfReference(IReadOnlyList<CardModel> candidates, CardModel card)
    {
        for (int i = 0; i < candidates.Count; i++)
        {
            if (ReferenceEquals(candidates[i], card))
            {
                return i;
            }
        }

        for (int i = 0; i < candidates.Count; i++)
        {
            if (candidates[i].Id == card.Id)
            {
                return i;
            }
        }

        return -1;
    }

    private static object? Source(AbstractModel? source)
    {
        if (source == null)
        {
            return null;
        }

        string kind = source switch
        {
            CardModel => "card",
            RelicModel => "relic",
            PotionModel => "potion",
            PowerModel => "power",
            EnchantmentModel => "enchantment",
            AfflictionModel => "affliction",
            _ => "model"
        };

        return new
        {
            kind,
            id = source.Id.ToString()
        };
    }

    private static string? Prompt(CardSelectionLogState state)
    {
        if (state.Prefs == null)
        {
            return null;
        }

        try
        {
            return state.Prefs.Value.Prompt.GetFormattedText();
        }
        catch
        {
            return state.Prefs.Value.Prompt.ToString();
        }
    }
}
