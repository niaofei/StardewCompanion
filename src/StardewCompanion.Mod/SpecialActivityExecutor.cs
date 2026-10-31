using System.Text.Json;
using System.Reflection;
using HarmonyLib;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;
using StardewCompanion.Fishing;
using StardewCompanion.Mod;
using StardewCompanion.Protocol;
using StardewMiningAgent.Combat;
using StardewMiningAgent.Models;
using StardewValley;
using StardewValley.Locations;
using StardewValley.Menus;
using StardewValley.Monsters;
using StardewValley.Tools;

namespace StardewCompanion.Mod;

/// <summary>Native game adapters for fishing, combat, and ordinary mine traversal.
/// Strategy inputs stay in the plan; high-frequency controls and verification stay local.</summary>
internal sealed class SpecialActivityExecutor
{
    private static SpecialActivityExecutor? _activeFishing;
    private static readonly FieldInfo? OldMouse = AccessTools.Field(typeof(Game1), "oldMouseState");
    private static readonly FieldInfo? OldKeyboard = AccessTools.Field(typeof(Game1), "oldKBState");
    private static readonly FishingPhysics NativeFishingPhysics = new();
    private static readonly PhasePlaneController NativeFishingController = new(NativeFishingPhysics, new TackleKit());
    private readonly string _skill;
    private readonly JsonElement _args;
    private readonly Action<GridPoint> _interact;
    private readonly Navigation _nav = new();
    private readonly Dictionary<string, int> _inventoryBefore;
    private readonly Dictionary<string, int> _gains = new();
    private readonly List<object> _events = new();
    private string _phase = "starting";
    private string _reason = "";
    private long _phaseAt;
    private long _lastProgress;
    private int _casts;
    private int _verifiedCatchCasts;
    private int _attackAttempts;
    private int _startFloor = -1;
    private int _startTime;
    private int _startDay;
    private Monster? _target;
    private int _targetHp;
    private bool _attackSent;
    private GridPoint? _approach;
    private string? _map;
    private int _targetFloor;

    internal int Completed { get; private set; }
    internal string Reason => _reason;
    internal string Phase => _phase;
    internal object Details => new { stage = _phase, castsAttempted = _casts, catchesVerified = _verifiedCatchCasts, killsVerified = _events.Count(e => e.ToString()!.Contains("kill_verified", StringComparison.Ordinal)), events = _events.ToArray(), actualGains = _gains };
    internal bool ProgressDue => Environment.TickCount64 - _lastProgress > 1000;
    internal void ProgressSent() => _lastProgress = Environment.TickCount64;
    internal static CapabilityStatus[] Capabilities() => new[]
    {
        new CapabilityStatus{Name="fish_at",Group="fishing",State="supported",Implemented=true,Reason=""},
        new CapabilityStatus{Name="fight_enemies",Group="combat",State="supported",Implemented=true,Reason=""},
        new CapabilityStatus{Name="mine_travel",Group="mine_travel",State="disabled",Implemented=true,Reason="mines_disabled"}
    };

    internal SpecialActivityExecutor(string skill, string arguments, Action<GridPoint> interact)
    {
        _skill = skill;
        _args = JsonDocument.Parse(arguments).RootElement.Clone();
        _interact = interact;
        _inventoryBefore = InventorySnapshot();
        if (skill == "fish_at")
        {
            _map = Text("location"); _targetFloor = Number("casts");
            if (_targetFloor is < 1 or > 20) throw new JsonException("casts_out_of_range");
            if (Text("direction") is not ("up" or "right" or "down" or "left")) throw new JsonException("invalid_cast_direction");
            if (Text("treasurePolicy") is not ("try" or "ignore")) throw new JsonException("invalid_treasure_policy");
        }
        if (skill == "fight_enemies")
        {
            if (Number("maxKills") is < 1 or > 30 || Number("maxGameMinutes") is < 1 or > 600) throw new JsonException("combat_limits_out_of_range");
            _startTime = Game1.timeOfDay;
            _startDay = Game1.Date.TotalDays;
        }
        if (skill == "mine_travel")
        {
            _targetFloor = Number("targetFloor");
            if (_targetFloor is < 0 or > 120) throw new JsonException("ordinary_mine_floor_out_of_range");
        }
    }

    internal string Tick(AiInputSimulator input)
    {
        if (_skill == "fish_at") return Fish(input);
        if (_skill == "fight_enemies") return Fight(input);
        return MineTravel(input);
    }

    private string Fish(AiInputSimulator input)
    {
        _activeFishing = this;
        var map = Text("location");
        var stand = new GridPoint(Number("x"), Number("y"));
        if (Game1.currentLocation.NameOrUniqueName != map)
        {
            _phase = "travel_to_shore";
            var travel = _nav.Move(map, stand, input, _interact, false);
            return travel is "blocked" or "unsupported" ? End(_nav.Reason) : "running";
        }
        if (Navigation.Current.Distance(stand) > 0)
        {
            _phase = "position_at_shore";
            var travel = _nav.Move(map, stand, input, _interact, false);
            return travel is "blocked" or "unsupported" ? End(_nav.Reason) : "running";
        }
        if (!Game1.currentLocation.IsOutdoors) return End("fishing_requires_observed_outdoor_shore");
        var rod = Game1.player.Items.OfType<FishingRod>().FirstOrDefault();
        if (rod == null) return End("fishing_rod_missing");
        Game1.player.faceDirection(Text("direction") switch { "up" => 0, "right" => 1, "down" => 2, _ => 3 });
        if (!ReferenceEquals(Game1.player.CurrentTool, rod)) Game1.player.CurrentToolIndex = Game1.player.Items.IndexOf(rod);
        CaptureFishGains();
        if (rod.fishCaught && _verifiedCatchCasts < _casts)
        {
            _verifiedCatchCasts++;
            Completed += Math.Max(1, rod.numberOfFishCaught);
            _events.Add(new { state = "native_fish_catch_verified", cast = _verifiedCatchCasts, itemId = rod.whichFish?.QualifiedItemId, count = Math.Max(1, rod.numberOfFishCaught), junk = rod.lastCatchWasJunk, treasure = rod.treasureCaught });
        }

        if (Game1.activeClickableMenu is BobberBar)
        {
            _phase = "native_fishing_minigame";
            return "running";
        }
        if (Game1.activeClickableMenu is DialogueBox)
        {
            _phase = "native_result_popup_left_for_model";
            return End("native_fishing_dialogue_observed", Completed > 0);
        }
        if (Game1.activeClickableMenu is ItemGrabMenu && rod.fishCaught)
            return End("native_fish_loot_menu_requires_inventory_space", Completed > 0);
        if (Game1.activeClickableMenu != null) return End("unexpected_fishing_menu:" + Game1.activeClickableMenu.GetType().Name);

        if (rod.isNibbling)
        {
            _phase = "hook_native_bite";
            input.Tool = true;
            _phaseAt = Environment.TickCount64;
            return "running";
        }
        if (rod.isFishing || rod.isReeling || rod.isCasting || rod.castedButBobberStillInAir)
        {
            _phase = rod.isReeling ? "native_reeling" : "waiting_for_bite";
            if (Environment.TickCount64 - _phaseAt > 90000) { input.Tool = true; return End("cast_timeout_no_native_catch"); }
            return "running";
        }
        if (_phase == "cast_release" && Environment.TickCount64 - _phaseAt < 1800) return "running";
        if (_casts >= Number("casts"))
        {
            return Completed > 0 ? End("requested_casts_completed", true) : End("no_native_catch_confirmed");
        }
        if (_phase is not ("cast_line" or "cast_release")) { _phase = "cast_line"; _phaseAt = Environment.TickCount64; }
        input.Tool = _phase == "cast_line";
        // Charge the cast, then release to invoke the game's normal cast logic.
        if (_phase == "cast_line" && Environment.TickCount64 - _phaseAt > 1400)
        {
            input.Tool = false;
            _casts++;
            _phaseAt = Environment.TickCount64;
            _phase = "cast_release";
            _events.Add(new { state = "native_cast_started", cast = _casts, direction = Text("direction") });
        }
        return "running";
    }

    private string Fight(AiInputSimulator input)
    {
        if (Game1.currentLocation is not MineShaft mine || mine.mineLevel is < 1 or > 120) return End("fight_requires_ordinary_mine");
        _phase = "engage_monster";
        var elapsedGameMinutes=(Game1.Date.TotalDays-_startDay)*1440+(Game1.timeOfDay/100*60+Game1.timeOfDay%100)-(_startTime/100*60+_startTime%100);
        if (elapsedGameMinutes >= Number("maxGameMinutes"))
            return End("model_selected_fight_time_limit_reached");
        if (Completed >= Number("maxKills")) return End("model_selected_kill_target_reached");
        var weapon = Game1.player.Items.OfType<MeleeWeapon>().FirstOrDefault(w => !w.isScythe() && w.QualifiedItemId == Text("weaponId"));
        if (weapon == null) return End("specified_melee_weapon_missing");
        if (!ReferenceEquals(Game1.player.CurrentTool, weapon)) Game1.player.CurrentToolIndex = Game1.player.Items.IndexOf(weapon);
        if (_target == null || !Game1.currentLocation.characters.Contains(_target))
        {
            if (_target != null)
            {
                if (_target.Health <= 0) { Completed++; _events.Add(new { state = "kill_verified", monster = _target.Name, floor = mine.mineLevel }); }
                else _events.Add(new { state = "target_disappeared_unverified", monster = _target.Name, hp = _target.Health });
                _target = null; _nav.Reset(); _approach = null;
                if (Completed >= Number("maxKills")) return End("model_selected_kill_target_reached");
            }
            _target = Game1.currentLocation.characters.OfType<Monster>().Where(m => m.Health > 0).OrderBy(m => Vector2.DistanceSquared(m.Position, Game1.player.Position)).FirstOrDefault();
            if (_target == null) return Completed > 0 ? End("no_more_visible_monsters") : End("no_visible_monsters");
            _targetHp = _target.Health; _attackAttempts = 0;
            _events.Add(new { state = "target_observed", monster = _target.Name, hp = _targetHp, floor = mine.mineLevel });
        }
        var playerTile = Navigation.Current;
        var enemyTile = new GridPoint(_target.TilePoint.X, _target.TilePoint.Y);
        var weaponKind = weapon.type.Value switch { 1 => WeaponKind.Dagger, 2 => WeaponKind.Club, _ => WeaponKind.Sword };
        var model = WeaponModel.Create(weaponKind);
        var delta = new StardewMiningAgent.Models.Vec2(enemyTile.X - playerTile.X, enemyTile.Y - playerTile.Y);
        var direction = Math.Abs(delta.X) >= Math.Abs(delta.Y) ? delta.X >= 0 ? Direction.Right : Direction.Left : delta.Y >= 0 ? Direction.Down : Direction.Up;
        if (model.CanHit(new(playerTile.X + .5f, playerTile.Y + .5f), direction, new(enemyTile.X + .5f, enemyTile.Y + .5f), .5f))
        {
            Game1.player.faceDirection(direction switch { Direction.Up => 0, Direction.Right => 1, Direction.Down => 2, _ => 3 });
            _phase = "native_weapon_attack";
            if (!_attackSent && !Game1.player.UsingTool)
            {
                input.Tool = true; _attackSent = true; _phaseAt = Environment.TickCount64; _attackAttempts++;
                _events.Add(new { state = "attack_sent", weapon = weapon.QualifiedItemId, target = _target.Name, hpBefore = _target.Health, attempt = _attackAttempts });
            }
            else if (Environment.TickCount64 - _phaseAt > 900 && !Game1.player.UsingTool)
            {
                if (_target.Health < _targetHp) { _targetHp = _target.Health; _events.Add(new { state = "native_hp_delta_verified", target = _target.Name, hp = _targetHp }); }
                else if (_attackAttempts >= 4) return End("native_attack_no_verified_hp_change");
                _attackSent = false;
            }
            return "running";
        }
        _attackSent = false;
        _approach ??= _nav.BestAdjacent(enemyTile);
        if (_approach == null) return End("monster_has_no_reachable_adjacent_tile");
        _phase = "approach_monster";
        var moved = _nav.Move(Game1.currentLocation.NameOrUniqueName, _approach.Value, input, _interact);
        return moved is "blocked" or "unsupported" ? End(_nav.Reason) : "running";
    }

    private string MineTravel(AiInputSimulator input)
    {
        if (_targetFloor > 0 && Game1.currentLocation is not MineShaft)
        {
            _phase = "travel_to_mine_entrance";
            var enter = _nav.Move(MineShaft.GetLevelName(1), new(0, 0), input, _interact);
            return enter is "blocked" or "unsupported" ? End(_nav.Reason) : "running";
        }
        if (Game1.currentLocation is not MineShaft mine)
        {
            if (_targetFloor == 0) return End("already_outside_mine");
            return End("mine_entrance_did_not_enter_ordinary_mine");
        }
        if (mine.mineLevel > 120) return End("unsupported_mine_type");
        _startFloor = mine.mineLevel;
        if (mine.mineLevel == _targetFloor) return End("target_floor_observed");
        if (_targetFloor == 0 || _targetFloor < mine.mineLevel || (_targetFloor % 5 == 0 && mine.tileBeneathElevator != Vector2.Zero))
        {
            if (mine.tileBeneathElevator == Vector2.Zero) return End("no_observed_elevator_on_this_floor");
            var elevator = Tile(mine.tileBeneathElevator);
            var adjacent = _nav.BestAdjacent(elevator);
            if (adjacent == null) return End("elevator_unreachable");
            _phase = "walk_to_unlocked_elevator";
            var step = _nav.Move(mine.NameOrUniqueName, adjacent.Value, input, _interact);
            if (step is "blocked" or "unsupported") return End(_nav.Reason);
            if (step == "reached")
            {
                _interact(elevator);
                if (Game1.activeClickableMenu is MineElevatorMenu menu)
                {
                    var choice = menu.elevators.FirstOrDefault(e => int.TryParse(e.name, out var floor) && floor == _targetFloor);
                    if (choice == null) return End("target_floor_not_unlocked_in_native_elevator");
                    menu.receiveLeftClick(choice.bounds.Center.X, choice.bounds.Center.Y);
                    _phase = "native_elevator_transition";
                }
            }
            return "running";
        }
        if (!mine.ladderHasSpawned) return End("no_observed_ladder_model_must_choose_whether_to_fight_or_replan");
        var ladder = Tile(mine.tileBeneathLadder);
        var approachLadder = _nav.BestAdjacent(ladder);
        if (approachLadder == null) return End("observed_ladder_unreachable");
        _phase = "walk_to_observed_ladder";
        var result = _nav.Move(mine.NameOrUniqueName, approachLadder.Value, input, _interact);
        if (result is "blocked" or "unsupported") return End(_nav.Reason);
        if (result == "reached") { _interact(ladder); _phase = "native_ladder_transition"; }
        return "running";
    }

    internal static bool BobberBarInput(BobberBar bar)
    {
        var active = _activeFishing;
        if (active == null || active._skill != "fish_at") return false;
        NativeFishingPhysics.SetNativeBarHeight(bar.bobberBarHeight);
        var state = new FishingState(
            BarPosition:(bar.bobberBarPos+bar.bobberBarHeight/2f)/568f,
            BarVelocity:bar.bobberBarSpeed/568f,
            FishPosition:Math.Clamp((bar.bobberPosition-2f)/568f,0f,1f),
            CatchProgress:bar.distanceFromCatching,
            TreasurePosition:bar.treasure?(float?)Math.Clamp((bar.treasurePosition-2f)/568f,0f,1f):null,
            TreasureProgress:bar.treasureCatchLevel,
            TreasureActive:bar.treasure&&!bar.treasureCaught);
        active._phase = "native_fishing_minigame";
        return NativeFishingController.Decide(state,active.Text("treasurePolicy")=="try"&&state.TreasureAvailable)==ControllerOutput.Press;
    }

    internal static bool OverrideNativeInput(out MouseState previousMouse, out KeyboardState previousKeyboard)
    {
        previousMouse = default; previousKeyboard=default;
        if (Game1.activeClickableMenu is not BobberBar bar || _activeFishing == null || OldMouse == null || OldKeyboard == null) return false;
        previousMouse = (MouseState)OldMouse.GetValue(null)!;
        previousKeyboard=(KeyboardState)OldKeyboard.GetValue(null)!;
        var pressed = BobberBarInput(bar);
        OldMouse.SetValue(null, new MouseState(0, 0, 0, pressed ? ButtonState.Pressed : ButtonState.Released,
            ButtonState.Released, ButtonState.Released, ButtonState.Released, ButtonState.Released));
        OldKeyboard.SetValue(null,default(KeyboardState));
        return true;
    }

    internal static void RestoreNativeInput(MouseState previousMouse,KeyboardState previousKeyboard)
    {OldMouse?.SetValue(null,previousMouse);OldKeyboard?.SetValue(null,previousKeyboard);}
    internal static void ClearActiveFishing() => _activeFishing = null;

    private int CaptureFishGains()
    {
        foreach (var (id, count) in InventorySnapshot())
        {
            var gain = Math.Max(0, count - _inventoryBefore.GetValueOrDefault(id));
            var added = Math.Max(0, gain - _gains.GetValueOrDefault(id));
            if (added == 0) continue;
            _gains[id] = gain; _events.Add(new { state = "inventory_gain_verified", itemId = id, count = added });
        }
        return _gains.Sum(p => p.Value);
    }

    private string End(string reason, bool success = false) { _reason = reason; _activeFishing = null; return success ? "completed" : Completed > 0 ? "partial" : "failed"; }
    private string Text(string key) => _args.GetProperty(key).GetString() ?? "";
    private int Number(string key) => _args.GetProperty(key).GetInt32();
    private static GridPoint Tile(Vector2 p) => new((int)p.X, (int)p.Y);
    private static Dictionary<string, int> InventorySnapshot() => Game1.player.Items.Where(i => i != null).GroupBy(i => i!.QualifiedItemId).ToDictionary(g => g.Key, g => g.Sum(i => i!.Stack));
}
