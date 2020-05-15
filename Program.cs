﻿using StardewModdingAPI;
using System.Collections.Generic;
using System;
using StardewValley;
using StardewModdingAPI.Events;
using Microsoft.Xna.Framework;
using System.Reflection;
using System.Linq;
using StardewValley.Locations;

namespace Lockpicks
{
    public class Mod : StardewModdingAPI.Mod
    {
        internal static JsonAssets JA;
        internal static Mod instance;
        internal static Random RNG = new Random(Guid.NewGuid().GetHashCode());

        internal static int LockpickItemId = -1;

        internal static HashSet<string> LockCache = new HashSet<string>();
        private string GenerateCacheKey(string objectType, GameLocation location, float x, float y) { return objectType + "^" + location.Name + "^" + ((int)x).ToString() + "^" + ((int)y).ToString(); }

        public override void Entry(IModHelper helper)
        {
            instance = this;

            //let's register the item with JsonAssets
            JA = new JsonAssets(this);
            if (!JA.IsHappy) return; //hook no events
            JA.RegisterObject(Helper.Translation.Get("tool"), Helper.Translation.Get("tooltip"), "lockpick.png", "Pierre", StardewValley.Object.junkCategory, 15, 500);
            
            Helper.Events.GameLoop.SaveLoaded += GameLoop_SaveLoaded;
            Helper.Events.GameLoop.DayStarted += new EventHandler<StardewModdingAPI.Events.DayStartedEventArgs>(delegate (object o, StardewModdingAPI.Events.DayStartedEventArgs a) { LockCache.Clear(); });
            Helper.Events.Input.ButtonPressed += Input_ButtonPressed;
            TileCheckAction += OnTileAction;
        }

        private static void Input_ButtonPressed(object sender, ButtonPressedEventArgs e)
        {
            if (e.IsSuppressed()) return; //already eaten by someone else.
            //check for Action activation
            if (!Game1.eventUp)
            {
                if (e.Button.IsActionButton() || e.Button.IsUseToolButton())
                {
                    if (Context.IsPlayerFree)
                    {
                        //get the target tile
                        Vector2 vector = new Vector2(Game1.getOldMouseX() + Game1.viewport.X, Game1.getOldMouseY() + Game1.viewport.Y) / 64f;
                        if (Game1.mouseCursorTransparency == 0f || !Game1.wasMouseVisibleThisFrame || (!Game1.lastCursorMotionWasMouse && (Game1.player.ActiveObject == null || (!Game1.player.ActiveObject.isPlaceable() && Game1.player.ActiveObject.Category != -74))))
                        {
                            vector = Game1.player.GetGrabTile();
                            if (vector.Equals(Game1.player.getTileLocation()))
                            {
                                vector = Utility.getTranslatedVector2(vector, Game1.player.FacingDirection, 1f);
                            }
                        }
                        if (!Utility.tileWithinRadiusOfPlayer((int)vector.X, (int)vector.Y, 1, Game1.player))
                        {
                            vector = Game1.player.GetGrabTile();
                            if (vector.Equals(Game1.player.getTileLocation()) && Game1.isAnyGamePadButtonBeingPressed())
                            {
                                vector = Utility.getTranslatedVector2(vector, Game1.player.FacingDirection, 1f);
                            }
                        }

                        //check Actions in tiledata
                        string value = Game1.currentLocation.doesTileHaveProperty((int)vector.X, (int)vector.Y, "Action", "Buildings");
                        if (!string.IsNullOrWhiteSpace(value))
                        {
                            var argsResult = TileCheckActionEvent(Game1.player, Game1.currentLocation, vector, value);
                            if (argsResult.Cancelled) { Mod.instance.Helper.Input.Suppress(e.Button); }
                        }
                        else
                        {
                            vector = Utility.getTranslatedVector2(vector, Game1.player.FacingDirection, 0f);
                            vector.Y += 1;
                            value = Game1.currentLocation.doesTileHaveProperty((int)vector.X, (int)vector.Y, "Action", "Buildings");
                            if (!string.IsNullOrWhiteSpace(value))
                            {
                                var argsResult = TileCheckActionEvent(Game1.player, Game1.currentLocation, vector, value);
                                if (argsResult.Cancelled) { Mod.instance.Helper.Input.Suppress(e.Button); }
                            }
                            else
                            {
                                vector = Game1.player.getTileLocation();
                                value = Game1.currentLocation.doesTileHaveProperty((int)vector.X, (int)vector.Y, "Action", "Buildings");
                                if (!string.IsNullOrWhiteSpace(value))
                                {
                                    var argsResult = TileCheckActionEvent(Game1.player, Game1.currentLocation, vector, value);
                                    if (argsResult.Cancelled) { Mod.instance.Helper.Input.Suppress(e.Button); }
                                }
                            }
                        }
                    }
                }
            }
        }

        private void GameLoop_SaveLoaded(object sender, StardewModdingAPI.Events.SaveLoadedEventArgs e)
        {
            LockpickItemId = JA.GetObjectId("Lockpick").Value;
            Monitor.Log("Lockpick item ID: " + LockpickItemId, LogLevel.Info);
            Helper.Events.Multiplayer.ModMessageReceived += Multiplayer_ModMessageReceived;
        }

        private void OnTileAction(object sender, TileCheckActionEventArgs args)
        {
            //Monitor.Log(args.Action, LogLevel.Alert);
            if (args.Cancelled) return; //already eaten by someone else
            var parameters = args.Action.Split(' ');
            if (parameters.Length < 1) return;
            bool lockFound = false;
            switch (parameters[0])
            {
                case "LockedDoorWarp":
                    if(parameters[3] == "WizardHouse" && !isWizardHouseUnlocked()) {
                        lockFound = true;
                        break;
                    }
                    if(parameters[3] == "SeedShop" && Game1.Date.DayOfWeek == DayOfWeek.Wednesday && !Game1.MasterPlayer.eventsSeen.Contains(191393))
                    {
                        lockFound = true;
                        break;
                    }
                    //check business hours
                    if (Game1.timeOfDay < int.Parse(parameters[4]) || Game1.timeOfDay >= int.Parse(parameters[5])) lockFound = true;
                    //check for a friendship requirement
                    else if(parameters.Length > 6 && Game1.player.getFriendshipLevelForNPC(parameters[6]) < int.Parse(parameters[7])) lockFound = true;
                    break;
                case "Door":
                    if (parameters.Length > 1 && Game1.player.getFriendshipLevelForNPC(parameters[1]) < 500) lockFound = true;
                    break;
                case "SkullDoor":
                    if (!args.Farmer.hasUnlockedSkullDoor && !args.Farmer.hasSkullKey) lockFound = true;
                    break;
                case "WarpCommunityCenter":
                    if (!(Game1.MasterPlayer.mailReceived.Contains("ccDoorUnlock") || Game1.MasterPlayer.mailReceived.Contains("JojaMember"))) lockFound = true;
                    break;
                case "WarpWomensLocker":
                    if (Game1.player.IsMale) lockFound = true;
                    break;
                case "WarpMensLocker":
                    if (!Game1.player.IsMale) lockFound = true;
                    break;
                case "WizardHatch":
                    if ((!Game1.player.friendshipData.ContainsKey("Wizard") || Game1.player.friendshipData["Wizard"].Points < 1000)) lockFound = true;
                    break;
                case "EnterSewer":
                    if (!Game1.player.hasRustyKey && !Game1.player.mailReceived.Contains("OpenedSewer")) lockFound = true;
                    break;
                case "Warp_Sunroom_Door":
                    if (Game1.player.getFriendshipHeartLevelForNPC("Caroline") < 2) lockFound = true;
                    break;
                case "Theater_Entrance":
                    if (!Game1.MasterPlayer.hasOrWillReceiveMail("ccMovieTheater")) break;
                    if (Game1.player.team.movieMutex.IsLocked() || Game1.isFestival() || Game1.timeOfDay > 2100 || Game1.timeOfDay < 900)
                    {
                        lockFound = true;
                        break;
                    }
                    if (!Game1.player.hasItemInInventory(809, 1, 0))
                    {
                        lockFound = true;
                        break;
                    }
                    if (Game1.player.lastSeenMovieWeek.Value >= Game1.Date.TotalWeeks) lockFound = true;
                    break;
            }
            //todo: forest sewer grate (town works), checks for tile id 1394 on forest map
            //todo: haley's dark room, requires loading temp map //Message "HaleyHouse.1"
            //todo: marnie's... coop thing, requires loading temp map //Message "AnimalShop.17"
            //todo: ruined jojamart (throws error in console at the moment)

            bool cached = LockCache.Contains(GenerateCacheKey(parameters[0], args.GameLocation, args.TileLocation.X, args.TileLocation.Y));
            if (!cached && !Game1.player.hasItemInInventory(LockpickItemId, 1)) return; //we're done here

            if (lockFound)
            {
                args.Cancelled = true;
                if (cached)
                {
                    OpenLock(args.Action.Split(' '), (int)args.TileLocation.X, (int)args.TileLocation.Y);
                }
                else
                {
                    string key = string.Join("^", new[] { "l", ((int)args.TileLocation.X).ToString(), ((int)args.TileLocation.Y).ToString(), args.Action });
                    Game1.currentLocation.lastQuestionKey = "lockpick";
                    Game1.currentLocation.createQuestionDialogue(Helper.Translation.Get("use"), new[] { new Response(key, Helper.Translation.Get("yes")), new Response("No", Helper.Translation.Get("no")) }, AcceptLockpick);
                }
            }
        }

        protected virtual bool isWizardHouseUnlocked()
        {
            if (Game1.player.mailReceived.Contains("wizardJunimoNote") || Game1.MasterPlayer.mailReceived.Contains("JojaMember"))
                return true;
            int num1 = Game1.MasterPlayer.mailReceived.Contains("ccFishTank") ? 1 : 0;
            bool flag1 = Game1.MasterPlayer.mailReceived.Contains("ccBulletin");
            bool flag2 = Game1.MasterPlayer.mailReceived.Contains("ccPantry");
            bool flag3 = Game1.MasterPlayer.mailReceived.Contains("ccVault");
            bool flag4 = Game1.MasterPlayer.mailReceived.Contains("ccBoilerRoom");
            bool flag5 = Game1.MasterPlayer.mailReceived.Contains("ccCraftsRoom");
            int num2 = flag1 ? 1 : 0;
            return (num1 & num2 & (flag2 ? 1 : 0) & (flag3 ? 1 : 0) & (flag4 ? 1 : 0) & (flag5 ? 1 : 0)) != 0;
        }

        public void AcceptLockpick(Farmer who, string answerKey)
        {
            if (answerKey == "No" || !answerKey.StartsWith("l^")) return; //wtf
            var parameters = answerKey.Substring(2).Split('^');
            if (parameters.Length != 3) return; //also wtf
            if (RNG.Next(30) == 0) //almost never breaks
            {
                Game1.player.removeFirstOfThisItemFromInventory(LockpickItemId);
                Game1.showRedMessage(Helper.Translation.Get("broke"));
                Game1.playSound("clank");
            }
            else OpenLock(parameters[2].Split(' '), int.Parse(parameters[0]), int.Parse(parameters[1]), true);
        }

        private void SendLockpickMessage(string cachekey)
        {
            Helper.Multiplayer.SendMessage<string>(cachekey, "lockpickEvent");
        }

        private void Multiplayer_ModMessageReceived(object sender, ModMessageReceivedEventArgs e)
        {
            string key = e.ReadAs<string>();
            if (!LockCache.Contains(key))
            {
                Game1.playSound("stoneCrack"); Game1.playSound("axchop");
                string[] split = key.Split('^');
                string lockType = split[0];
                if(lockType != "SkullDoor") LockCache.Add(key);
                if (lockType == "Door")
                {
                    Game1.currentLocation.map.GetLayer("Back").Tiles[int.Parse(split[2]), int.Parse(split[3])].Properties.Remove("TouchAction");
                }
            }
        }

        private void OpenLock(string[] Lock, int tileX, int tileY, bool picked = false)
        {
            if (picked)
            {
                Game1.playSound("stoneCrack"); Game1.playSound("axchop");
                string key = GenerateCacheKey(Lock[0], Game1.currentLocation, tileX, tileY);
                LockCache.Add(key);
                if (Game1.IsMultiplayer) SendLockpickMessage(key);
            }
            if (Lock[0] == "LockedDoorWarp")
            {
                int tx = int.Parse(Lock[1]); int ty = int.Parse(Lock[2]);
                WarpFarmer(Lock[3], tx, ty);
            }
            else if (Lock[0] == "Door")
            {
                Game1.currentLocation.openDoor(new xTile.Dimensions.Location(tileX, tileY), playSound: !picked);
                Game1.currentLocation.map.GetLayer("Back").Tiles[tileX, tileY].Properties.Remove("TouchAction");
            }
            else if (Lock[0] == "SkullDoor")
            {
                Game1.showRedMessage(Helper.Translation.Get("complex"));
            }
            else if (Lock[0] == "WarpCommunityCenter")
            {
                if (!picked) Game1.playSound("doorClose");
                WarpFarmer("CommunityCenter", 32, 23);
            }
            else if (Lock[0] == "WarpWomensLocker" || Lock[0] == "WarpMensLocker")
            {
                if (Lock.Length < 5 && !picked) Game1.playSound("doorClose");
                WarpFarmer(Lock[3], Convert.ToInt32(Lock[1]), Convert.ToInt32(Lock[2]));
            }
            else if (Lock[0] == "WizardHatch")
            {
                if (!picked) Game1.playSound("doorClose");
                WarpFarmer("WizardHouseBasement", 4, 4);
            }
            else if (Lock[0] == "EnterSewer")
            {
                if (!picked) Game1.playSound("stairsdown");
                WarpFarmer("Sewer", 16, 11);
            }
            else if (Lock[0] == "Warp_Sunroom_Door")
            {
                if (!picked) Game1.playSound("doorClose");
                WarpFarmer("Sunroom", 5, 13);
            }
            else if (Lock[0] == "Theater_Entrance")
            {
                if (!picked) Game1.playSound("doorClose");
                WarpFarmer("MovieTheater", 13, 15);
            }
        }

        public static void WarpFarmer(string location, int x, int y)
        {
            GameLocation destination = Game1.getLocationFromName(location);
            Game1.warpFarmer(new LocationRequest(destination.NameOrUniqueName, destination.uniqueName.Value != null, destination), x, y, 2);
        }

        //Tile Check Action - called when a player activates a tile with an Action property. Cancellable.
        public static event TileCheckActionHandler TileCheckAction;
        public delegate void TileCheckActionHandler(object sender, TileCheckActionEventArgs args);
        internal static TileCheckActionEventArgs TileCheckActionEvent(Farmer who, GameLocation location, Vector2 tile, string action)
        {
            var args = new TileCheckActionEventArgs
            {
                GameLocation = location,
                TileLocation = tile,
                Action = action,
                Farmer = who,
                Cancelled = false
            };
            if (TileCheckAction != null) TileCheckAction.Invoke(Mod.instance, args);
            return args;
        }

        public class TileCheckActionEventArgs
        {
            public GameLocation GameLocation { set; get; }
            public Vector2 TileLocation { set; get; }
            public string Action { get; set; }
            public Farmer Farmer { set; get; }
            public bool Cancelled { get; set; }
        }
    }
}