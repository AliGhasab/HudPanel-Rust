// Reference: 0Harmony
// Requires: ImageLibrary (optional but recommended), Economics (optional), ServerRewards (optional), TruePVE (optional), TimedPermissions (optional)
// Author: ChatGPT (HUD Panel Plus)
// Version: 1.2.0
// Description: A modern, themeable HUD panel for Rust with rich status widgets, event icons, balance, map position, FPS, PVP/PVE, custom hooks, live in-game admin editor, rotating announcements with progress, ImageLibrary icons, and theme profiles.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Oxide.Core;
using Oxide.Core.Libraries.Covalence;
using Oxide.Core.Plugins;
using UnityEngine;
using Newtonsoft.Json;
using Oxide.Game.Rust.Cui;

namespace Oxide.Plugins
{
    [Info("HUD Panel Plus", "AliGhasab", "1.0.0")]
    [Description("Modern, pretty and extensible HUD panel")]    
    public class HUDPanelPlus : RustPlugin
    {
        #region Plugins
        [PluginReference] private readonly Plugin ImageLibrary;
        [PluginReference] private readonly Plugin Economics;
        [PluginReference] private readonly Plugin ServerRewards;
        [PluginReference] private readonly Plugin TruePVE;
        [PluginReference] private readonly Plugin TimedPermissions;
        #endregion

        #region Data & Config
        private const string PERM_USE = "hudpanelplus.use";
        private const string PERM_ADMIN = "hudpanelplus.admin";

        private StoredConfig Cfg;
        private PersistentData Data;

        private const string ADMIN_PANEL_ID = "HUDPP.Admin";
        private const string HUD_ROOT_ID   = "HUDPP.Root";
        private const string ANNOUNCE_ID   = "HUDPP.Announce";

        public class StoredConfig
        {
            [JsonProperty("ChatCommand")] public string ChatCommand = "hud";
            [JsonProperty("AdminCommand")] public string AdminCommand = "hudadmin";
            [JsonProperty("ThemeCommand")] public string ThemeCommand = "hudtheme";
            [JsonProperty("AnnounceCommand")] public string AnnounceCommand = "announce";

            [JsonProperty("ToggleKeyBinding")] public string ToggleKeyBinding = "h"; // client-side bind suggestion
            [JsonProperty("UpdateInterval")] public float UpdateInterval = 0.5f;
            [JsonProperty("BaseScalePercent")] public int BaseScalePercent = 48; // 30-100
            [JsonProperty("IconSpacing")] public int IconSpacing = 18;
            [JsonProperty("TimeFormat")] public string TimeFormat = "HH:mm"; // 24h default

            [JsonProperty("ShowFPS")] public bool ShowFPS = true;
            [JsonProperty("ShowOnline")] public bool ShowOnline = true;
            [JsonProperty("ShowSleepers")] public bool ShowSleepers = true;
            [JsonProperty("ShowBalance")] public bool ShowBalance = true;
            [JsonProperty("ShowPveState")] public bool ShowPveState = true;
            [JsonProperty("ShowMapCoords")] public bool ShowMapCoords = true;
            [JsonProperty("ShowWaypointDistance")] public bool ShowWaypointDistance = true;

            [JsonProperty("EnableEvents")] public bool EnableEvents = true;
            [JsonProperty("EnableCustomEvents")] public bool EnableCustomEvents = true;

            [JsonProperty("Anchor")] public Anchor AnchorPosition = Anchor.CenterTop;
            [JsonProperty("Theme")] public ThemeConfig Theme = new ThemeConfig();

            // Theme profiles: name -> theme
            [JsonProperty("ThemeProfiles")] public Dictionary<string, ThemeConfig> ThemeProfiles = new Dictionary<string, ThemeConfig>
            {
                {"HUD", ThemeConfig.PresetHud()},
                {"BASIC", ThemeConfig.PresetBasic()},
                {"CUBE", ThemeConfig.PresetCube()},
                {"TRIANGLE", ThemeConfig.PresetTriangle()},
            };
            [JsonProperty("DefaultThemeProfile")] public string DefaultThemeProfile = "HUD";

            // Rotating announcements
            [JsonProperty("AnnouncementsEnabled")] public bool AnnouncementsEnabled = true;
            [JsonProperty("AnnouncementDuration")] public float AnnouncementDuration = 8f; // seconds per message
            [JsonProperty("Announcements")] public List<string> Announcements = new List<string>{"Welcome to the server!","Type /hud to toggle the new HUD.","Be kind. No cheating."};

            // ImageLibrary icons (name -> url). Auto-loaded at server init.
            [JsonProperty("Icons")] public Dictionary<string,string> IconUrls = new Dictionary<string, string>
            {
                {"online","https://i.imgur.com/8bKxgqX.png"},
                {"sleep","https://i.imgur.com/1mC4l7z.png"},
                {"money","https://i.imgur.com/5k0d8eR.png"},
                {"clock","https://i.imgur.com/8gq1bJH.png"},
                {"map","https://i.imgur.com/0eQkJ2G.png"},
                {"waypoint","https://i.imgur.com/Pk4Zl8P.png"},
                {"heli","https://i.imgur.com/6m8bYx3.png"},
            };
        }

        public class PersistentData
        {
            public string ActiveThemeProfile = null; // if null, use config default
            public float AnnounceTimer = 0f;
            public int AnnounceIndex = 0;
        }

        public enum Anchor { LeftTop, CenterTop, RightTop, LeftBottom, CenterBottom, RightBottom }

        public class ThemeConfig
        {
            [JsonProperty("Preset")] public PresetTheme Preset = PresetTheme.HUD; // HUD, BASIC, CUBE, TRIANGLE
            [JsonProperty("Color1")] public string Color1 = "#0b0b0b";   // background
            [JsonProperty("Color2")] public string Color2 = "#3f51b5";   // accent / progress
            [JsonProperty("Color3")] public string Color3 = "#ffffff33"; // subtle overlay
            [JsonProperty("IconColor")] public string IconColor = "#ffffffff";
            [JsonProperty("Opacity1")] public float Opacity1 = 0.35f;
            [JsonProperty("Opacity2")] public float Opacity2 = 1.0f;
            [JsonProperty("Opacity3")] public float Opacity3 = 0.05f;
            [JsonProperty("CornerRadius")] public float CornerRadius = 8f; // purely visual (affects image slices)
            [JsonProperty("FontSize")] public int FontSize = 12;
            [JsonProperty("FontAlign")] public TextAnchor FontAlign = TextAnchor.MiddleCenter;
            [JsonProperty("BarThickness")] public int BarThickness = 7; // 1-15

            public static ThemeConfig PresetHud() => new ThemeConfig{Preset=PresetTheme.HUD, Color1="#0b0b0b", Color2="#3f51b5", Color3="#ffffff33", IconColor="#ffffffff", Opacity1=0.35f, Opacity2=1f, Opacity3=0.05f, FontSize=12, BarThickness=7};
            public static ThemeConfig PresetBasic()=> new ThemeConfig{Preset=PresetTheme.BASIC, Color1="#101010", Color2="#00bcd4", Color3="#ffffff22", IconColor="#ffffffff", Opacity1=0.28f, Opacity2=1f, Opacity3=0.04f, FontSize=12, BarThickness=5};
            public static ThemeConfig PresetCube() => new ThemeConfig{Preset=PresetTheme.CUBE, Color1="#050505", Color2="#ff6f00", Color3="#ffffff22", IconColor="#ffffffff", Opacity1=0.40f, Opacity2=1f, Opacity3=0.08f, FontSize=13, BarThickness=9};
            public static ThemeConfig PresetTriangle()=> new ThemeConfig{Preset=PresetTheme.TRIANGLE, Color1="#0a0a12", Color2="#9c27b0", Color3="#ffffff22", IconColor="#ffffffff", Opacity1=0.32f, Opacity2=1f, Opacity3=0.07f, FontSize=12, BarThickness=8};
        }

        public enum PresetTheme { HUD, BASIC, CUBE, TRIANGLE }

        public class CustomEvent
        {
            [JsonProperty("Enabled")] public bool Enabled = true;
            [JsonProperty("Order")] public int Order = 0;
            [JsonProperty("OnStartHook")] public string OnStartHook;
            [JsonProperty("OnEndHook")] public string OnEndHook;
            [JsonProperty("IconKey")] public string IconKey = null; // maps to IconUrls / ImageLibrary key
        }

        protected override void LoadDefaultConfig() => Cfg = new StoredConfig();
        protected override void LoadConfig()
        {
            base.LoadConfig();
            try { Cfg = Config.ReadObject<StoredConfig>(); }
            catch { PrintWarning("Config file corrupt, creating new"); Cfg = new StoredConfig(); }
            SaveConfig();
        }
        protected override void SaveConfig() => Config.WriteObject(Cfg, true);

        private void LoadData()
        {
            Data = Interface.Oxide.DataFileSystem.ReadObject<PersistentData>(Name) ?? new PersistentData();
        }
        private void SaveData() => Interface.Oxide.DataFileSystem.WriteObject(Name, Data);
        #endregion

        #region Runtime State
        private class PlayerUI
        {
            public BasePlayer Player;
            public bool Visible;
            public Vector3? Waypoint;
            public float FpsSmoothed = 60f;
            public PlayerUI(BasePlayer p) { Player = p; }
        }
        private readonly Dictionary<ulong, PlayerUI> _uis = new Dictionary<ulong, PlayerUI>();

        // Event flags (built-in + custom)
        private readonly Dictionary<string, bool> _eventStates = new Dictionary<string, bool>
        {
            { "CargoPlane", false },
            { "CargoShip", false },
            { "CH47", false },
            { "PatrolHeli", false },
            { "Bradley", false }
        };
        #endregion

        #region Hooks
        private void Init()
        {
            permission.RegisterPermission(PERM_USE, this);
            permission.RegisterPermission(PERM_ADMIN, this);
            LoadData();

            AddCovalenceCommand(Cfg.ChatCommand, nameof(CmdHud));
            AddCovalenceCommand("setwp", nameof(CmdWaypoint));
            AddCovalenceCommand("clearwp", nameof(CmdClearWaypoint));
            AddCovalenceCommand(Cfg.AdminCommand, nameof(CmdAdmin));
            AddCovalenceCommand(Cfg.ThemeCommand, nameof(CmdTheme));
            AddCovalenceCommand(Cfg.AnnounceCommand, nameof(CmdAnnounce));
        }

        private void OnServerInitialized()
        {
            EnsureIcons();
            foreach (var p in BasePlayer.activePlayerList)
                EnsureUI(p);
            timer.Every(Cfg.UpdateInterval, Tick);
        }

        private void Unload()
        {
            foreach (var p in BasePlayer.activePlayerList)
            {
                DestroyUI(p);
                CuiHelper.DestroyUi(p, ADMIN_PANEL_ID);
            }
            _uis.Clear();
            SaveData();
        }

        private void OnPlayerConnected(BasePlayer player)
        {
            EnsureUI(player);
        }

        private void OnPlayerDisconnected(BasePlayer player, string reason)
        {
            DestroyUI(player);
            CuiHelper.DestroyUi(player, ADMIN_PANEL_ID);
        }

        // Spawn/Despawn events
        private void OnEntitySpawned(BaseNetworkable entity)
        {
            if (!Cfg.EnableEvents || entity == null) return;
            if (entity is CargoPlane) _eventStates["CargoPlane"] = true;
            else if (entity is CargoShip) _eventStates["CargoShip"] = true;
            else if (entity is CH47HelicopterAIController) _eventStates["CH47"] = true;
            else if (entity is PatrolHelicopterAI) _eventStates["PatrolHeli"] = true;
            else if (entity is BradleyAPC) _eventStates["Bradley"] = true;
        }

        private void OnEntityKill(BaseNetworkable entity)
        {
            if (!Cfg.EnableEvents || entity == null) return;
            if (entity is CargoPlane) _eventStates["CargoPlane"] = false;
            else if (entity is CargoShip) _eventStates["CargoShip"] = false;
            else if (entity is CH47HelicopterAIController) _eventStates["CH47"] = false;
            else if (entity is PatrolHelicopterAI) _eventStates["PatrolHeli"] = false;
            else if (entity is BradleyAPC) _eventStates["Bradley"] = false;
        }
        #endregion

        #region Commands
        private void CmdHud(IPlayer iplayer, string cmd, string[] args)
        {
            var player = iplayer?.Object as BasePlayer;
            if (player == null) return;
            if (!iplayer.HasPermission(PERM_USE)) { iplayer.Reply("You don't have permission to use HUD."); return; }

            var ui = EnsureUI(player);
            ui.Visible = !ui.Visible;
            if (ui.Visible) BuildUI(player);
            else DestroyUI(player);
        }

        private void CmdWaypoint(IPlayer iplayer, string cmd, string[] args)
        {
            var player = iplayer?.Object as BasePlayer; if (player == null) return;
            var ui = EnsureUI(player);
            Vector3 pos;
            if (args.Length == 3 && float.TryParse(args[0], out var x) && float.TryParse(args[1], out var y) && float.TryParse(args[2], out var z))
                pos = new Vector3(x, y, z);
            else pos = player.transform.position; // set to current position
            ui.Waypoint = pos;
            iplayer.Reply($"Waypoint set to {pos}");
        }

        private void CmdClearWaypoint(IPlayer iplayer, string cmd, string[] args)
        {
            var player = iplayer?.Object as BasePlayer; if (player == null) return;
            var ui = EnsureUI(player);
            ui.Waypoint = null;
            iplayer.Reply("Waypoint cleared");
        }

        private void CmdAdmin(IPlayer iplayer, string cmd, string[] args)
        {
            var player = iplayer?.Object as BasePlayer; if (player == null) return;
            if (!iplayer.HasPermission(PERM_ADMIN)) { iplayer.Reply("No permission."); return; }
            ToggleAdminPanel(player);
        }

        private void CmdTheme(IPlayer iplayer, string cmd, string[] args)
        {
            if (!iplayer.HasPermission(PERM_ADMIN)) { iplayer.Reply("No permission."); return; }
            if (args.Length == 0) { iplayer.Reply("Usage: /"+Cfg.ThemeCommand+" list|load <name>|save <name>|delete <name>"); return; }
            var sub = args[0].ToLower();
            if (sub == "list")
            {
                var list = string.Join(", ", Cfg.ThemeProfiles.Keys.OrderBy(s=>s));
                iplayer.Reply("Themes: "+list);
            }
            else if (sub == "load" && args.Length>=2)
            {
                var name = args[1];
                if (Cfg.ThemeProfiles.TryGetValue(name, out var th))
                {
                    Cfg.Theme = CloneTheme(th);
                    Data.ActiveThemeProfile = name; SaveData(); SaveConfig();
                    BroadcastRebuild();
                    iplayer.Reply($"Theme '{name}' loaded.");
                }
                else iplayer.Reply("Theme not found.");
            }
            else if (sub == "save" && args.Length>=2)
            {
                var name = args[1];
                Cfg.ThemeProfiles[name] = CloneTheme(Cfg.Theme);
                Data.ActiveThemeProfile = name; SaveData(); SaveConfig();
                iplayer.Reply($"Theme '{name}' saved.");
            }
            else if (sub == "delete" && args.Length>=2)
            {
                var name = args[1];
                if (Cfg.ThemeProfiles.Remove(name)) { SaveConfig(); iplayer.Reply($"Theme '{name}' deleted."); }
                else iplayer.Reply("Theme not found.");
            }
            else iplayer.Reply("Invalid subcommand.");
        }

        private void CmdAnnounce(IPlayer iplayer, string cmd, string[] args)
        {
            if (!iplayer.HasPermission(PERM_ADMIN)) { iplayer.Reply("No permission."); return; }
            if (args.Length == 0) { iplayer.Reply("Usage: /"+Cfg.AnnounceCommand+" add <text>|remove <index>|start|stop|list"); return; }
            var sub = args[0].ToLower();
            if (sub == "add")
            {
                if (args.Length < 2) { iplayer.Reply("Usage: /"+Cfg.AnnounceCommand+" add <text>"); return; }
                var text = string.Join(" ", args.Skip(1));
                Cfg.Announcements.Add(text); SaveConfig();
                iplayer.Reply("Added.");
            }
            else if (sub == "remove" && args.Length>=2 && int.TryParse(args[1], out var idx))
            {
                if (idx>=0 && idx<Cfg.Announcements.Count) { Cfg.Announcements.RemoveAt(idx); SaveConfig(); iplayer.Reply("Removed."); }
                else iplayer.Reply("Index out of range.");
            }
            else if (sub == "start") { Cfg.AnnouncementsEnabled = true; SaveConfig(); iplayer.Reply("Announcements started."); }
            else if (sub == "stop") { Cfg.AnnouncementsEnabled = false; SaveConfig(); HideAnnouncementsAll(); iplayer.Reply("Announcements stopped."); }
            else if (sub == "list")
            {
                for (int i=0;i<Cfg.Announcements.Count;i++) iplayer.Reply($"[{i}] {Cfg.Announcements[i]}");
            }
            else iplayer.Reply("Invalid subcommand.");
        }
        #endregion

        #region Core
        private PlayerUI EnsureUI(BasePlayer player)
        {
            if (!_uis.TryGetValue(player.userID, out var ui))
            {
                ui = new PlayerUI(player) { Visible = true };
                _uis[player.userID] = ui;
            }
            return ui;
        }

        private void DestroyUI(BasePlayer player)
        {
            CuiHelper.DestroyUi(player, HUD_ROOT_ID);
            CuiHelper.DestroyUi(player, ANNOUNCE_ID);
        }

        private void Tick()
        {
            foreach (var kv in _uis)
            {
                var ui = kv.Value; if (!ui.Visible || ui.Player == null || !ui.Player.IsConnected) continue;
                // Smooth FPS estimation per player (client-ish proxy via server frame time)
                var fps = (int)Mathf.Clamp(1f / Mathf.Max(Time.smoothDeltaTime, 0.0001f), 10f, 240f);
                ui.FpsSmoothed = Mathf.Lerp(ui.FpsSmoothed, fps, 0.2f);

                RefreshUI(ui.Player, ui);
            }

            // Announcements rotation/progress
            if (Cfg.AnnouncementsEnabled && Cfg.Announcements.Count > 0)
            {
                Data.AnnounceTimer += Cfg.UpdateInterval;
                if (Data.AnnounceTimer >= Cfg.AnnouncementDuration)
                {
                    Data.AnnounceTimer = 0f;
                    Data.AnnounceIndex = (Data.AnnounceIndex + 1) % Cfg.Announcements.Count;
                }
                foreach (var p in BasePlayer.activePlayerList)
                    DrawAnnouncement(p);
            }
        }
        #endregion

        #region UI Build
        private void BuildUI(BasePlayer player)
        {
            DestroyUI(player);
            var container = new CuiElementContainer();

            var theme = GetActiveTheme();
            var anchor = GetAnchor(Cfg.AnchorPosition);
            var scale = Mathf.Clamp(Cfg.BaseScalePercent, 30, 100) / 100f;

            // Root panel
            var panel = new CuiPanel
            {
                Image = { Color = HexToColor(theme.Color1, theme.Opacity1) },
                RectTransform = { AnchorMin = anchor.min, AnchorMax = anchor.max, OffsetMin = new Vector2(-300f*scale, -70f*scale), OffsetMax = new Vector2(300f*scale, 70f*scale) },
                CursorEnabled = false
            };
            var root = container.Add(panel, "Overlay", HUD_ROOT_ID);

            // Accent bar
            container.Add(new CuiPanel
            {
                Image = { Color = HexToColor(theme.Color2, theme.Opacity2) },
                RectTransform = { AnchorMin = "0 0", AnchorMax = "1 0.08" }
            }, root);

            // Content rows
            var x = 0f; float step = 0.16f; // 6 columns
            if (Cfg.ShowOnline) AddSmallStat(container, root, ref x, step, "Online", BasePlayer.activePlayerList.Count.ToString(), "online");
            if (Cfg.ShowSleepers) AddSmallStat(container, root, ref x, step, "Sleepers", BasePlayer.sleepingPlayerList.Count.ToString(), "sleep");
            if (Cfg.ShowBalance) AddSmallStat(container, root, ref x, step, "Balance", GetBalance(player).ToString(), "money");
            if (Cfg.ShowPveState) AddSmallStat(container, root, ref x, step, "Mode", IsPve() ? "PVE" : "PVP", null);
            if (Cfg.ShowFPS) AddSmallStat(container, root, ref x, step, "FPS", ((int)GetFpsEstimate()).ToString(), null);
            AddSmallStat(container, root, ref x, step, "Time", DateTime.Now.ToString(Cfg.TimeFormat, CultureInfo.InvariantCulture), "clock");

            // Second row: coords, waypoint, events
            float yRow = 0.08f; float rowH = 0.92f - yRow;
            var content = container.Add(new CuiPanel
            {
                Image = { Color = HexToColor(theme.Color3, theme.Opacity3) },
                RectTransform = { AnchorMin = $"{0f} {yRow}", AnchorMax = "1 1" }
            }, root);

            // Map coords
            if (Cfg.ShowMapCoords)
            {
                var grid = GetGrid(player.transform.position);
                AddCard(container, content, 0f, 0.33f, $"{grid}", "Position", "map");
            }

            // Waypoint distance
            var ui = _uis[player.userID];
            if (Cfg.ShowWaypointDistance)
            {
                string distText = ui.Waypoint.HasValue ? (Vector3.Distance(player.transform.position, ui.Waypoint.Value)).ToString("F0") + " m" : "—";
                AddCard(container, content, 0.33f, 0.66f, distText, "Waypoint", "waypoint");
            }

            // Events block
            if (Cfg.EnableEvents)
            {
                var active = GetActiveEvents();
                AddCard(container, content, 0.66f, 1f, string.Join("  ", active), "Events", "heli");
            }

            CuiHelper.AddUi(player, container);

            // Announcement bar (drawn in Tick as well for progress)
            if (Cfg.AnnouncementsEnabled && Cfg.Announcements.Count>0)
                DrawAnnouncement(player);
        }

        private void RefreshUI(BasePlayer player, PlayerUI ui)
        {
            // For simplicity, rebuild; could be optimized by updating labels only
            BuildUI(player);
        }

        private void AddSmallStat(CuiElementContainer c, string parent, ref float x, float step, string title, string value, string iconKey)
        {
            var theme = GetActiveTheme();
            var minX = x; var maxX = Mathf.Min(1f, x + step);
            var panel = new CuiPanel
            {
                Image = { Color = "0 0 0 0" },
                RectTransform = { AnchorMin = $"{minX} 0", AnchorMax = $"{maxX} 0.22" }
            };
            var id = c.Add(panel, parent);

            // Optional icon (ImageLibrary)
            if (!string.IsNullOrEmpty(iconKey))
            {
                var png = GetImagePng(iconKey);
                if (!string.IsNullOrEmpty(png))
                {
                    c.Add(new CuiElement
                    {
                        Parent = id,
                        Components =
                        {
                            new CuiRawImageComponent{ Png = png },
                            new CuiRectTransformComponent{ AnchorMin = "0.02 0.15", AnchorMax = "0.14 0.9" }
                        }
                    });
                }
            }

            AddText(c, id, value, 14, 0.16f, 0.95f, 0.10f, 0.80f, theme.IconColor, theme.FontAlign, shadow:true);
            AddText(c, id, title, 10, 0f, 1f, 0.80f, 1f, "#ffffffaa", TextAnchor.MiddleCenter, shadow:false);
            x += step;
        }

        private void AddCard(CuiElementContainer c, string parent, float minX, float maxX, string bigText, string title, string iconKey=null)
        {
            var theme = GetActiveTheme();
            var p = new CuiPanel
            {
                Image = { Color = HexToColor(theme.Color1, theme.Opacity1) },
                RectTransform = { AnchorMin = $"{minX} 0.1", AnchorMax = $"{maxX} 0.98" }
            };
            var id = c.Add(p, parent);

            // Accent bar on left
            c.Add(new CuiPanel
            {
                Image = { Color = HexToColor(theme.Color2, theme.Opacity2) },
                RectTransform = { AnchorMin = "0 0", AnchorMax = "0.015 1" }
            }, id);

            if (!string.IsNullOrEmpty(iconKey))
            {
                var png = GetImagePng(iconKey);
                if (!string.IsNullOrEmpty(png))
                {
                    c.Add(new CuiElement
                    {
                        Parent = id,
                        Components =
                        {
                            new CuiRawImageComponent{ Png = png },
                            new CuiRectTransformComponent{ AnchorMin = "0.02 0.15", AnchorMax = "0.08 0.85" }
                        }
                    });
                }
            }

            AddText(c, id, bigText, 16, 0.05f, 0.98f, 0.1f, 0.8f, theme.IconColor, theme.FontAlign, shadow:true);
            AddText(c, id, title, 10, 0.05f, 0.6f, 0.8f, 0.98f, "#ffffffaa", TextAnchor.MiddleLeft, shadow:false);
        }

        private void AddText(CuiElementContainer c, string parent, string text, int size, float minX, float maxX, float minY, float maxY, string color, TextAnchor align, bool shadow)
        {
            if (shadow)
            {
                // Poor-man shadow: duplicate label with slight offset and darker color
                c.Add(new CuiLabel
                {
                    Text = { Text = text, FontSize = size, Align = align, Color = "0 0 0 0.65" },
                    RectTransform = { AnchorMin = $"{minX+0.003f} {minY-0.01f}", AnchorMax = $"{maxX+0.003f} {maxY-0.01f}" }
                }, parent);
            }
            c.Add(new CuiLabel
            {
                Text = { Text = text, FontSize = size, Align = align, Color = HexToColor(color, 1f) },
                RectTransform = { AnchorMin = $"{minX} {minY}", AnchorMax = $"{maxX} {maxY}" }
            }, parent);
        }
        #endregion

        #region Announcement UI
        private void DrawAnnouncement(BasePlayer player)
        {
            CuiHelper.DestroyUi(player, ANNOUNCE_ID);
            var theme = GetActiveTheme();

            if (!Cfg.AnnouncementsEnabled || Cfg.Announcements.Count==0) return;
            var msg = Cfg.Announcements[Mathf.Clamp(Data.AnnounceIndex, 0, Cfg.Announcements.Count-1)];
            var progress = Mathf.Clamp01(Data.AnnounceTimer / Mathf.Max(0.001f, Cfg.AnnouncementDuration));

            var cont = new CuiElementContainer();

            // Bottom center bar
            var panel = new CuiPanel
            {
                Image = { Color = HexToColor(theme.Color1, theme.Opacity1) },
                RectTransform = { AnchorMin = "0.25 0.02", AnchorMax = "0.75 0.08" },
                CursorEnabled = false
            };
            var root = cont.Add(panel, "Overlay", ANNOUNCE_ID);

            // Progress (fills from left to right)
            cont.Add(new CuiPanel
            {
                Image = { Color = HexToColor(theme.Color2, theme.Opacity2) },
                RectTransform = { AnchorMin = "0 0", AnchorMax = $"{0.001f + 0.999f*progress} 0.08" }
            }, root);

            // Message text with shadow
            AddText(cont, root, msg, theme.FontSize+2, 0.03f, 0.97f, 0.18f, 0.92f, theme.IconColor, TextAnchor.MiddleCenter, shadow:true);

            CuiHelper.AddUi(player, cont);
        }

        private void HideAnnouncementsAll()
        {
            foreach (var p in BasePlayer.activePlayerList) CuiHelper.DestroyUi(p, ANNOUNCE_ID);
        }
        #endregion

        #region Admin Panel (live editor)
        private void ToggleAdminPanel(BasePlayer player)
        {
            // Toggle
            bool open = IsAdminPanelOpen(player);
            if (open) { CuiHelper.DestroyUi(player, ADMIN_PANEL_ID); return; }

            var theme = GetActiveTheme();
            var c = new CuiElementContainer();

            var root = c.Add(new CuiPanel
            {
                Image = { Color = "0 0 0 0.75" },
                RectTransform = { AnchorMin = "0.72 0.12", AnchorMax = "0.98 0.88" },
                CursorEnabled = true
            }, "Overlay", ADMIN_PANEL_ID);

            AddText(c, root, "HUD Panel Plus — Admin", 16, 0.04f, 0.96f, 0.9f, 0.98f, "#ffffff", TextAnchor.MiddleCenter, shadow:true);

            // Buttons (simple actions)
            float y = 0.84f;
            AddButton(c, root, 0.05f, 0.45f, y, y-0.07f, "Preset: Next", "hudpp.btn.preset"); y-=0.08f;
            AddButton(c, root, 0.55f, 0.95f, y, y-0.07f, "Anchor: Next", "hudpp.btn.anchor"); y-=0.08f;
            AddButton(c, root, 0.05f, 0.45f, y, y-0.07f, "Accent +", "hudpp.btn.accentp");
            AddButton(c, root, 0.55f, 0.95f, y, y-0.07f, "Accent -", "hudpp.btn.accentm"); y-=0.08f;
            AddButton(c, root, 0.05f, 0.45f, y, y-0.07f, "Font +", "hudpp.btn.fontp");
            AddButton(c, root, 0.55f, 0.95f, y, y-0.07f, "Font -", "hudpp.btn.fontm"); y-=0.08f;
            AddButton(c, root, 0.05f, 0.95f, y, y-0.07f, "Save Theme As…", "hudpp.btn.savetheme"); y-=0.08f;
            AddButton(c, root, 0.05f, 0.95f, y, y-0.07f, "Close", "hudpp.btn.close");

            CuiHelper.AddUi(player, c);
        }

        private bool IsAdminPanelOpen(BasePlayer player) => false; // simple toggle; we destroy on close

        private void AddButton(CuiElementContainer c, string parent, float minX, float maxX, float maxY, float minY, string text, string cmd)
        {
            var theme = GetActiveTheme();
            var panel = new CuiPanel
            {
                Image = { Color = HexToColor(theme.Color2, 0.85f) },
                RectTransform = { AnchorMin = $"{minX} {minY}", AnchorMax = $"{maxX} {maxY}" }
            };
            var id = c.Add(panel, parent);

            c.Add(new CuiButton
            {
                Button = { Color = HexToColor(theme.Color2, 0f), Command = $"hudpp_ui {cmd}", Close = "" },
                RectTransform = { AnchorMin = "0 0", AnchorMax = "1 1" },
                Text = { Text = text, Align = TextAnchor.MiddleCenter, FontSize = GetActiveTheme().FontSize+0 }
            }, id);
        }

        [ConsoleCommand("hudpp_ui")]
        private void CCmd_UI(ConsoleSystem.Arg arg)
        {
            var player = arg.Player(); if (player == null) return;
            if (!permission.UserHasPermission(player.UserIDString, PERM_ADMIN)) return;
            if (arg.Args == null || arg.Args.Length == 0) return;
            var action = string.Join(" ", arg.Args);

            switch (action)
            {
                case "hudpp.btn.close":
                    CuiHelper.DestroyUi(player, ADMIN_PANEL_ID);
                    break;
                case "hudpp.btn.preset":
                    CyclePreset(); BroadcastRebuild(); ToggleAdminPanel(player); break;
                case "hudpp.btn.anchor":
                    CycleAnchor(); BroadcastRebuild(); ToggleAdminPanel(player); break;
                case "hudpp.btn.fontp":
                    Cfg.Theme.FontSize = Mathf.Clamp(Cfg.Theme.FontSize+1, 10, 20); SaveConfig(); BroadcastRebuild(); ToggleAdminPanel(player); break;
                case "hudpp.btn.fontm":
                    Cfg.Theme.FontSize = Mathf.Clamp(Cfg.Theme.FontSize-1, 10, 20); SaveConfig(); BroadcastRebuild(); ToggleAdminPanel(player); break;
                case "hudpp.btn.accentp":
                    // lighten accent (simple): raise opacity a bit
                    Cfg.Theme.Opacity2 = Mathf.Clamp01(Cfg.Theme.Opacity2 + 0.05f); SaveConfig(); BroadcastRebuild(); ToggleAdminPanel(player); break;
                case "hudpp.btn.accentm":
                    Cfg.Theme.Opacity2 = Mathf.Clamp01(Cfg.Theme.Opacity2 - 0.05f); SaveConfig(); BroadcastRebuild(); ToggleAdminPanel(player); break;
                case "hudpp.btn.savetheme":
                    // Save with timestamp name
                    var name = $"Theme_{DateTime.UtcNow:HHmmss}";
                    Cfg.ThemeProfiles[name] = CloneTheme(Cfg.Theme); Data.ActiveThemeProfile = name; SaveConfig(); SaveData(); BroadcastRebuild(); ToggleAdminPanel(player);
                    SendReply(player, $"Saved current theme as {name}");
                    break;
            }
        }

        private void BroadcastRebuild()
        {
            foreach (var p in BasePlayer.activePlayerList)
                if (_uis.TryGetValue(p.userID, out var ui) && ui.Visible) BuildUI(p);
        }

        private void CyclePreset()
        {
            var order = new[]{PresetTheme.HUD, PresetTheme.BASIC, PresetTheme.CUBE, PresetTheme.TRIANGLE};
            int idx = Array.IndexOf(order, Cfg.Theme.Preset);
            idx = (idx + 1) % order.Length;
            var next = order[idx];
            ThemeConfig src = next switch
            {
                PresetTheme.HUD => ThemeConfig.PresetHud(),
                PresetTheme.BASIC => ThemeConfig.PresetBasic(),
                PresetTheme.CUBE => ThemeConfig.PresetCube(),
                _ => ThemeConfig.PresetTriangle()
            };
            Cfg.Theme = CloneTheme(src);
            SaveConfig();
        }

        private void CycleAnchor()
        {
            var order = new[]{Anchor.LeftTop, Anchor.CenterTop, Anchor.RightTop, Anchor.LeftBottom, Anchor.CenterBottom, Anchor.RightBottom};
            int idx = Array.IndexOf(order, Cfg.AnchorPosition);
            idx = (idx + 1) % order.Length;
            Cfg.AnchorPosition = order[idx];
            SaveConfig();
        }
        #endregion

        #region Helpers
        private ThemeConfig GetActiveTheme()
        {
            if (!string.IsNullOrEmpty(Data.ActiveThemeProfile) && Cfg.ThemeProfiles.TryGetValue(Data.ActiveThemeProfile, out var th))
                return th;
            return Cfg.Theme;
        }

        private ThemeConfig CloneTheme(ThemeConfig t)
        {
            return new ThemeConfig
            {
                Preset=t.Preset, Color1=t.Color1, Color2=t.Color2, Color3=t.Color3, IconColor=t.IconColor,
                Opacity1=t.Opacity1, Opacity2=t.Opacity2, Opacity3=t.Opacity3, CornerRadius=t.CornerRadius,
                FontSize=t.FontSize, FontAlign=t.FontAlign, BarThickness=t.BarThickness
            };
        }

        private float GetFpsEstimate() => Mathf.Clamp(1f / Mathf.Max(Time.smoothDeltaTime, 0.0001f), 10f, 240f);

        private bool IsPve()
        {
            // If TruePVE is installed and returns true, consider PVE; else use ConVar.Server.pve
            if (TruePVE != null)
            {
                try { var b = (bool?)TruePVE?.Call("IsPVEServer"); if (b.HasValue) return b.Value; } catch { }
            }
            return ConVar.Server.pve;
        }

        private int GetBalance(BasePlayer player)
        {
            if (Economics != null)
            {
                try { var bal = (double?)Economics?.Call("Balance", player.UserIDString); if (bal.HasValue) return Mathf.RoundToInt((float)bal.Value); } catch {}
            }
            if (ServerRewards != null)
            {
                try { var points = (int?)ServerRewards?.Call("CheckPoints", player.userID); if (points.HasValue) return points.Value; } catch {}
            }
            return 0;
        }

        private string GetGrid(Vector3 pos)
        {
            var map = TerrainMeta.Size.x; // world size
            int i = Mathf.Clamp((int)((pos.x + map / 2f) / (map / 26f)), 0, 25);
            int j = Mathf.Clamp((int)((map - (pos.z + map / 2f)) / (map / 26f)) + 1, 1, 26);
            char letter = (char)('A' + i);
            return $"{letter}{j}";
        }

        private (string min, string max) GetAnchor(Anchor a)
        {
            switch (a)
            {
                case Anchor.LeftTop: return ("0.02 0.88", "0.45 0.98");
                case Anchor.CenterTop: return ("0.28 0.88", "0.72 0.98");
                case Anchor.RightTop: return ("0.55 0.88", "0.98 0.98");
                case Anchor.LeftBottom: return ("0.02 0.02", "0.45 0.12");
                case Anchor.CenterBottom: return ("0.28 0.02", "0.72 0.12");
                case Anchor.RightBottom: return ("0.55 0.02", "0.98 0.12");
            }
            return ("0.28 0.88", "0.72 0.98");
        }

        private string HexToColor(string hex, float alpha)
        {
            Color c = Color.white;
            if (ColorUtility.TryParseHtmlString(hex, out var outc)) c = outc;
            c.a = alpha;
            return $"{c.r} {c.g} {c.b} {c.a}";
        }

        private void EnsureIcons()
        {
            if (ImageLibrary == null) return;
            foreach (var kv in Cfg.IconUrls)
            {
                try { ImageLibrary?.Call("AddImage", kv.Value, kv.Key, (ulong)0); } catch {}
            }
        }

        private string GetImagePng(string key)
        {
            if (ImageLibrary == null) return null;
            try
            {
                // Try both common APIs
                var png = ImageLibrary?.Call("GetImage", key) as string;
                if (string.IsNullOrEmpty(png)) png = ImageLibrary?.Call("GetPng", key) as string;
                return png;
            }
            catch { return null; }
        }
        #endregion

        #region API (Custom events enable/disable)
        // External plugins can toggle icons by calling:
        //   object o = Interface.CallHook("HUDPP_ToggleCustomEvent", "MyEvent", true);
        private object HUDPP_ToggleCustomEvent(string key, bool state)
        {
            if (!Cfg.EnableCustomEvents) return null;
            _eventStates[key] = state;
            return true;
        }
        #endregion

        #region Admin UI (state expose)
        private object HUDPP_GetState(BasePlayer player)
        {
            return new
            {
                visible = _uis.TryGetValue(player.userID, out var ui) && ui.Visible,
                waypoint = _uis.TryGetValue(player.userID, out var ui2) ? ui2.Waypoint : (Vector3?)null,
                themeProfile = Data.ActiveThemeProfile ?? Cfg.DefaultThemeProfile
            };
        }
        #endregion
    }
}
