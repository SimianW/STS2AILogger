using System;
using System.Collections.Generic;
using System.Linq;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.Runs;

namespace STS2AILogger.STS2AILoggerCode.Logging;

public static class GameSnapshots
{
    public static object? Run(RunState? runState)
    {
        if (runState == null)
        {
            return null;
        }

        return new
        {
            game_mode = runState.GameMode.ToString(),
            ascension = runState.AscensionLevel,
            act_index = runState.CurrentActIndex,
            act_id = Id(runState.Act),
            act_floor = runState.ActFloor,
            total_floor = runState.TotalFloor,
            room = Room(runState.CurrentRoom),
            players = runState.Players.Select(Player).ToList()
        };
    }

    public static object? Combat(CombatState? combatState)
    {
        if (combatState == null)
        {
            return null;
        }

        return new
        {
            encounter = Id(combatState.Encounter),
            round = combatState.RoundNumber,
            side = combatState.CurrentSide.ToString(),
            run = RunBasics(combatState.RunState as RunState),
            players = combatState.Players.Select(Player).ToList(),
            enemies = combatState.Enemies.Select(Creature).ToList()
        };
    }

    public static object? Player(Player? player)
    {
        if (player == null)
        {
            return null;
        }

        PlayerCombatState? combat = player.PlayerCombatState;
        bool inCombat = player.Creature.CombatState != null && combat != null;

        return new
        {
            net_id = player.NetId,
            character = Id(player.Character),
            hp = player.Creature.CurrentHp,
            max_hp = player.Creature.MaxHp,
            block = player.Creature.Block,
            gold = player.Gold,
            in_combat = inCombat,
            energy = inCombat ? combat!.Energy : (int?)null,
            max_energy = inCombat ? combat!.MaxEnergy : player.MaxEnergy,
            stars = inCombat ? combat!.Stars : (int?)null,
            deck = Cards(player.Deck.Cards),
            hand = inCombat ? Cards(combat!.Hand.Cards) : new List<object>(),
            draw_pile = inCombat ? Cards(combat!.DrawPile.Cards) : new List<object>(),
            discard_pile = inCombat ? Cards(combat!.DiscardPile.Cards) : new List<object>(),
            exhaust_pile = inCombat ? Cards(combat!.ExhaustPile.Cards) : new List<object>(),
            relics = player.Relics.Select(Id).ToList(),
            potions = player.PotionSlots.Select(p => p == null ? null : Id(p)).ToList(),
            powers = Powers(player.Creature)
        };
    }

    public static object Card(CardModel? card)
    {
        if (card == null)
        {
            return new { id = (string?)null };
        }

        return new
        {
            id = Id(card),
            type = card.Type.ToString(),
            rarity = card.Rarity.ToString(),
            upgraded = card.IsUpgraded,
            upgrade_level = card.CurrentUpgradeLevel,
            pile = card.Pile?.Type.ToString(),
            enchantment = card.Enchantment == null ? null : Id(card.Enchantment),
            affliction = card.Affliction == null ? null : Id(card.Affliction)
        };
    }

    public static List<object> Cards(IEnumerable<CardModel>? cards)
    {
        return cards?.Select(Card).ToList() ?? new List<object>();
    }

    public static object? Room(AbstractRoom? room)
    {
        if (room == null)
        {
            return null;
        }

        return new
        {
            id = room.Id,
            type = room.RoomType.ToString(),
            model = room.ModelId?.ToString(),
            is_victory = room.IsVictoryRoom,
            is_pre_finished = room.IsPreFinished
        };
    }

    public static object? Creature(Creature? creature)
    {
        if (creature == null)
        {
            return null;
        }

        return new
        {
            combat_id = creature.CombatId,
            model = creature.ModelId.ToString(),
            name = creature.LogName,
            side = creature.Side.ToString(),
            hp = creature.CurrentHp,
            max_hp = creature.MaxHp,
            block = creature.Block,
            alive = creature.IsAlive,
            powers = Powers(creature),
            intent = creature.Monster?.NextMove?.Id
        };
    }

    public static object CardPlay(CardPlay? cardPlay)
    {
        if (cardPlay == null)
        {
            return new { card = (object?)null };
        }

        return new
        {
            card = Card(cardPlay.Card),
            target = Creature(cardPlay.Target),
            result_pile = cardPlay.ResultPile.ToString(),
            is_auto_play = cardPlay.IsAutoPlay,
            play_index = cardPlay.PlayIndex,
            play_count = cardPlay.PlayCount,
            resources = new
            {
                energy_spent = cardPlay.Resources.EnergySpent,
                energy_value = cardPlay.Resources.EnergyValue,
                stars_spent = cardPlay.Resources.StarsSpent,
                star_value = cardPlay.Resources.StarValue
            }
        };
    }

    private static object? RunBasics(RunState? runState)
    {
        if (runState == null)
        {
            return null;
        }

        return new
        {
            act_index = runState.CurrentActIndex,
            act_floor = runState.ActFloor,
            total_floor = runState.TotalFloor,
            room = Room(runState.CurrentRoom)
        };
    }

    private static List<object> Powers(Creature creature)
    {
        return creature.Powers.Select(power => new
        {
            id = Id(power),
            amount = power.Amount,
            display_amount = power.DisplayAmount
        }).Cast<object>().ToList();
    }

    private static string? Id(AbstractModel? model)
    {
        return model?.Id.ToString();
    }
}
