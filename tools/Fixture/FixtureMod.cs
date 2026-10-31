using System.Text.Json;
using StardewModdingAPI;
using StardewModdingAPI.Events;
using StardewValley;
using StardewValley.Menus;
using StardewValley.Network;
using StardewValley.TerrainFeatures;
using Microsoft.Xna.Framework;

namespace StardewCompanion.Fixture;

public sealed partial class FixtureMod : Mod
{
    private int _ticks;
    private bool _started;
    private bool _saving;
    private IEnumerator<int>? _save;
    private string _mode = "";
    private bool _joined;

    public override void Entry(IModHelper helper)
    {
        _mode = Environment.GetEnvironmentVariable("SC_M0_FIXTURE") ?? "";
        if (_mode is not ("create" or "join" or "solo5")) return;
        helper.Events.GameLoop.UpdateTicked += Tick;
    }

    private void Tick(object? sender, UpdateTickedEventArgs e)
    {
        _ticks++;
        if(_mode=="solo5"){SoloTick();return;}
        if (_mode == "join") { JoinTick(); return; }
        if (!_started && _ticks > 240 && Game1.activeClickableMenu is TitleMenu title)
        {
            // Only a NEW, dedicated test character. Never load or alter an existing save.
            var existing = Directory.GetDirectories(Program.GetSavesFolder(), "SCM0_*");
            if (existing.Length != 0) { Monitor.Log("Fixture already exists; refusing to overwrite.", LogLevel.Error); _started = true; return; }
            _started = true;
            Game1.player.Name = "M0Host";
            Game1.player.displayName = "M0Host";
            Game1.player.farmName.Value = "SCM0";
            Game1.player.favoriteThing.Value = "Testing";
            Game1.player.isCustomized.Value = true;
            Game1.player.ConvertClothingOverrideToClothesItems();
            Game1.whichFarm = 0;
            Game1.startingCabins = 1;
            Game1.multiplayerMode = 0;
            title.createdNewCharacter(true);
            Monitor.Log("Created dedicated SCM0 test world using the normal new-game path.", LogLevel.Info);
        }
        if (_started && !_saving && Context.IsWorldReady && Game1.player.Name == "M0Host" && _ticks > 900)
        {
            _saving = true;
            _save = SaveGame.Save();
        }
        if (_save != null && !_save.MoveNext())
        {
            _save.Dispose();
            _save = null;
            var evidencePath = Environment.GetEnvironmentVariable("SC_M0_FIXTURE_RESULT");
            if (!string.IsNullOrWhiteSpace(evidencePath))
                File.WriteAllText(evidencePath, JsonSerializer.Serialize(new { saveId = Game1.uniqueIDForThisGame.ToString(),
                    player = Game1.player.Name, farm = Game1.player.farmName.Value, day = Game1.Date.TotalDays,
                    folder = Path.Combine(Program.GetSavesFolder(), SaveGame.FilterFileName(Game1.GetSaveGameName()) + "_" + Game1.uniqueIDForThisGame),
                    method = "normal new-game + SaveGame.Save; no custom save types" }));
            Monitor.Log("M0 fixture saved. Closing the fixture creator.", LogLevel.Info);
            GameRunner.instance.Exit();
        }
    }

    private void JoinTick()
    {
        if(_ticks%15==0)LogicTick();
        if (_ticks % 60 == 0 && Context.IsWorldReady && Environment.GetEnvironmentVariable("SC_M1_PREPARE") == "1"
            && Game1.uniqueIDForThisGame.ToString() == Environment.GetEnvironmentVariable("SC_M0_EXPECTED_SAVE")
            && Game1.currentLocation.NameOrUniqueName == "Farm")
        {
            var marker = Path.ChangeExtension(Environment.GetEnvironmentVariable("SC_M0_FIXTURE_RESULT"), ".m1-prepared.json");
            if (!string.IsNullOrWhiteSpace(marker) && !File.Exists(marker))
            {
                var l = Game1.currentLocation;
                var cropTile = new Vector2(52,18); Vector2? free = null;
                for(var y=17;y<=22&&free==null;y++)for(var x=51;x<=59&&free==null;x++)
                {var p=new Vector2(x,y);if(p!=cropTile&&p!=Game1.player.Tile&&!l.objects.ContainsKey(p)&&!l.terrainFeatures.ContainsKey(p)&&l.isTilePassable(new xTile.Dimensions.Location(x,y),Game1.viewport)&&!Game1.getFarm().buildings.Any(b=>b.occupiesTile(p)))free=p;}
                if (free.HasValue && !l.objects.ContainsKey(cropTile)
                    && (!l.terrainFeatures.TryGetValue(cropTile,out var a) || a is HoeDirt { crop: null })
                    )
                {
                    var forageTile=free.Value;
                    var mature = new HoeDirt(0,l) { crop = new Crop("472",52,18,l) }; mature.crop.growCompletely();
                    l.terrainFeatures[cropTile] = mature;
                    var forage = (StardewValley.Object)ItemRegistry.Create("(O)16"); forage.IsSpawnedObject = true; forage.TileLocation = forageTile;
                    l.objects.Add(forageTile,forage);
                    var can = Game1.player.Items.OfType<StardewValley.Tools.WateringCan>().FirstOrDefault(); if(can!=null)can.WaterLeft=0;
                    File.WriteAllText(marker,JsonSerializer.Serialize(new{saveId=Game1.uniqueIDForThisGame.ToString(),mature=new{x=52,y=18},forage=new{x=forageTile.X,y=forageTile.Y},water=0,purpose="Explicit M1 test setup only; native routines must do all work"}));
                    Monitor.Log("Prepared explicit M1 routine samples in SCM0 test save only.",LogLevel.Info);
                }
            }
        }
        if (_ticks % 60 == 0 && Context.IsWorldReady && Environment.GetEnvironmentVariable("SC_M0_PREPARE_FIXTURES") == "1"
            && Game1.uniqueIDForThisGame.ToString() == Environment.GetEnvironmentVariable("SC_M0_EXPECTED_SAVE")
            && Game1.currentLocation.NameOrUniqueName == "Farm")
        {
            var marker = Path.ChangeExtension(Environment.GetEnvironmentVariable("SC_M0_FIXTURE_RESULT"), ".prepared.json");
            if (!string.IsNullOrWhiteSpace(marker) && !File.Exists(marker))
            {
                // Explicit integration-test setup, never a production skill or model tool.
                // Use only the already-tilled test square; don't overwrite player objects/terrain.
                var cropTile = new Vector2(52, 18);
                var location = Game1.currentLocation;
                Vector2? freeChestTile = null;
                for (var y = 17; y <= 22 && freeChestTile == null; y++)
                    for (var x = 51; x <= 59 && freeChestTile == null; x++)
                    {
                        var candidate = new Vector2(x, y);
                        if (candidate == cropTile || candidate == Game1.player.Tile) continue;
                        if (!location.objects.ContainsKey(candidate) && !location.terrainFeatures.ContainsKey(candidate)
                            && location.isTilePassable(new xTile.Dimensions.Location(x, y), Game1.viewport)
                            && !Game1.getFarm().buildings.Any(b => b.occupiesTile(candidate))) freeChestTile = candidate;
                    }
                if (location.terrainFeatures.TryGetValue(cropTile, out var terrain) && terrain is HoeDirt dirt && dirt.crop == null
                    && freeChestTile.HasValue)
                {
                    var chestTile = freeChestTile.Value;
                    dirt.crop = new Crop("472", 52, 18, location);
                    dirt.crop.growCompletely();
                    var chest = new StardewValley.Objects.Chest(true) { TileLocation = chestTile };
                    chest.Items.Add(ItemRegistry.Create("(O)388", 5));
                    location.objects.Add(chestTile, chest);
                    File.WriteAllText(marker, JsonSerializer.Serialize(new { saveId = Game1.uniqueIDForThisGame.ToString(),
                        crop = "prepared mature parsnip at 52,18", chest = new { x = chestTile.X, y = chestTile.Y, wood = 5 },
                        purpose = "Integration-test fixture only. The AI must harvest and transfer through normal interactions." }));
                    Monitor.Log("Prepared explicit M0 crop and chest samples in SCM0 test save only.", LogLevel.Info);
                }
            }
        }
        if (_ticks % 60 == 0 && Context.IsWorldReady)
        {
            var output = Environment.GetEnvironmentVariable("SC_M0_FIXTURE_RESULT");
            if (!string.IsNullOrWhiteSpace(output))
                File.WriteAllText(output, JsonSerializer.Serialize(new {
                    date = Game1.Date.TotalDays, time = Game1.timeOfDay,
                    location = Game1.currentLocation.NameOrUniqueName,
                    player = new { x = Game1.player.TilePoint.X, y = Game1.player.TilePoint.Y, stamina = Game1.player.Stamina,
                        isInBed = Game1.player.isInBed.Value, tool = Game1.player.CurrentTool?.Name },
                    toolTarget = new { x = (int)Game1.player.GetToolLocation().X / 64, y = (int)Game1.player.GetToolLocation().Y / 64 },
                    soil = Game1.currentLocation.terrainFeatures.Pairs.Where(p => p.Value is HoeDirt).Select(p => new {
                        x = p.Key.X, y = p.Key.Y, wet = ((HoeDirt)p.Value).state.Value,
                        crop = ((HoeDirt)p.Value).crop?.indexOfHarvest.Value,
                        phase = ((HoeDirt)p.Value).crop?.currentPhase.Value }),
                    chests = Game1.currentLocation.objects.Pairs.Where(p => p.Value is StardewValley.Objects.Chest).Select(p => new {
                        x = p.Key.X, y = p.Key.Y,
                        items = ((StardewValley.Objects.Chest)p.Value).Items.Select(i => new { id = i?.QualifiedItemId, stack = i?.Stack ?? 0 }) })
                }));
        }
        if (!_started && _ticks > 240 && Game1.activeClickableMenu is TitleMenu)
        {
            _started = true;
            var multiplayer = Helper.Reflection.GetField<Multiplayer>(typeof(Game1), "multiplayer").GetValue();
            TitleMenu.subMenu = new FarmhandMenu(multiplayer.InitClient(new LidgrenClient("127.0.0.1")));
            Monitor.Log("M0 join assist: requesting normal LAN farmhand list.", LogLevel.Info);
        }
        if (_ticks % 60 != 0) return;
        var menu = Game1.activeClickableMenu is TitleMenu ? TitleMenu.subMenu : Game1.activeClickableMenu;
        if (!_joined && menu is FarmhandMenu farmhands)
        {
            var slots = Helper.Reflection.GetField<List<LoadGameMenu.MenuSlot>>(farmhands, "menuSlots").GetValue();
            var choice = slots.OfType<FarmhandMenu.FarmhandSlot>().FirstOrDefault(s => !s.BelongsToAnotherPlayer()
                && (!s.Farmer.isCustomized.Value || s.Farmer.Name == "M0AI"));
            if (choice != null)
            {
                _joined = true;
                choice.Activate();
                Monitor.Log("M0 join assist: selected an unclaimed or matching test farmhand.", LogLevel.Info);
            }
        }
        if (_joined && menu is CharacterCustomization character)
        {
            Helper.Reflection.GetField<TextBox>(character, "nameBox").GetValue().Text = "M0AI";
            Helper.Reflection.GetField<TextBox>(character, "favThingBox").GetValue().Text = "Testing";
            Helper.Reflection.GetMethod(character, "optionButtonClick").Invoke("OK");
            Monitor.Log("M0 join assist: submitted normal farmhand customization.", LogLevel.Info);
        }
    }
}
