using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;
using System.Linq;
using Oxide.Core;
using Oxide.Core.Plugins;
using Oxide.Game.Rust.Cui;
using UnityEngine;

namespace Oxide.Plugins
{
    [Info("Dawn Terminal", "YourName", "1.0.0")]
    [Description("Immersive DAWN SYSTEM terminal interface with boot sequence")]
    class DawnTerminal : RustPlugin
    {
        #region Fields
        
        [PluginReference] private Plugin DawnSys;
        
        private const string UI_MAIN = "DawnTerminal.Main";
        private const string UI_BOOT = "DawnTerminal.Boot";
        private const string UI_LOGIN = "DawnTerminal.Login";
        private const string UI_REGISTER = "DawnTerminal.Register";
        
        private Dictionary<ulong, int> playerPages = new Dictionary<ulong, int>();
        private Dictionary<ulong, bool> hasSeenBoot = new Dictionary<ulong, bool>();
        private Dictionary<ulong, int> typingLineIndex = new Dictionary<ulong, int>();
        private Dictionary<ulong, Timer> typingTimers = new Dictionary<ulong, Timer>();
        private Dictionary<ulong, string> playerInputUsername = new Dictionary<ulong, string>();
        private Dictionary<ulong, string> playerInputPassword = new Dictionary<ulong, string>();
        
        private StoredData storedData;
        
        private class StoredData
        {
            public Dictionary<ulong, PlayerCredentials> PlayerCredentials = new Dictionary<ulong, PlayerCredentials>();
        }
        
        private class PlayerCredentials
        {
            public string Username { get; set; }
            public string PasswordHash { get; set; }
            public bool HasRegistered { get; set; }
        }
        
        private class TerminalPage
        {
            public string Title { get; set; }
            public List<string> Content { get; set; }
        }
        
        private List<TerminalPage> pages = new List<TerminalPage>();
        
        #endregion
        
        #region Oxide Hooks
        
        void Init()
        {
            InitializePages();
            LoadData();
        }
        
        void OnPlayerConnected(BasePlayer player)
        {
            if (player == null) return;
            
            timer.Once(1.5f, () =>
            {
                if (player != null && player.IsConnected)
                {
                    // Check if player has registered credentials
                    if (storedData.PlayerCredentials.ContainsKey(player.userID) && 
                        storedData.PlayerCredentials[player.userID].HasRegistered)
                    {
                        // Show login screen
                        ShowLoginScreen(player);
                    }
                    else
                    {
                        // Show registration screen for first-time users
                        ShowRegistrationScreen(player);
                    }
                }
            });
        }
        
        void Unload()
        {
            foreach (var player in BasePlayer.activePlayerList)
            {
                DestroyUI(player);
                
                // Clean up typing timers
                if (typingTimers.ContainsKey(player.userID))
                {
                    typingTimers[player.userID]?.Destroy();
                    typingTimers.Remove(player.userID);
                }
            }
            SaveData();
        }
        
        #endregion
        
        #region Data Management
        
        private void LoadData()
        {
            storedData = Interface.Oxide.DataFileSystem.ReadObject<StoredData>("DawnTerminal");
            if (storedData == null)
                storedData = new StoredData();
        }
        
        private void SaveData()
        {
            Interface.Oxide.DataFileSystem.WriteObject("DawnTerminal", storedData);
        }
        
        private string HashPassword(string password)
        {
            using (SHA256 sha256 = SHA256.Create())
            {
                byte[] bytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(password));
                StringBuilder builder = new StringBuilder();
                for (int i = 0; i < bytes.Length; i++)
                {
                    builder.Append(bytes[i].ToString("x2"));
                }
                return builder.ToString();
            }
        }
        
        private string GetPowerGridStatus(BasePlayer player)
        {
            // Debug: Check if DawnSys is loaded
            if (DawnSys == null)
            {
                Puts("[DawnTerminal] DawnSys plugin reference is null");
                return "<color=red>POWER GRID: [OFFLINE]</color> // DawnSys plugin not loaded";
            }
            
            Puts($"[DawnTerminal] DawnSys plugin IS loaded: {DawnSys.Name} v{DawnSys.Version}");
            
            // Debug: Try to call the API
            Puts($"[DawnTerminal] Calling DawnSys API for {player.displayName} ({player.userID})");
            var stats = DawnSys.Call("API_GetPlayerPowerStats", player.userID);
            
            Puts($"[DawnTerminal] API returned: {(stats == null ? "NULL" : stats.GetType().Name)}");
            
            if (stats == null)
            {
                Puts($"[DawnTerminal] API returned null for player {player.displayName}");
                return "<color=yellow>POWER GRID: [NO DATA]</color> // Purchase generators/batteries in /dawnsys";
            }
            
            var statsDict = stats as Dictionary<string, object>;
            if (statsDict == null)
            {
                Puts($"[DawnTerminal] Could not cast stats to Dictionary");
                return "<color=yellow>POWER GRID: [ERROR]</color> // Data format error";
            }
            
            // Debug: Show what we got
            Puts($"[DawnTerminal] Stats received: {statsDict.Count} entries");
            
            int generators = Convert.ToInt32(statsDict["generators"]);
            int batteries = Convert.ToInt32(statsDict["batteries"]);
            int activeBatteries = Convert.ToInt32(statsDict["activeBatteries"]);
            float charge = Convert.ToSingle(statsDict["charge"]);
            float fuel = Convert.ToSingle(statsDict["fuelStored"]);
            float maxFuel = Convert.ToSingle(statsDict["maxFuelCapacity"]);
            
            string chargeBar = CreateProgressBar(charge / 100f, 12);
            string fuelBar = CreateProgressBar(fuel / maxFuel, 12);
            
            string result = $"GEN:[{generators}] BAT:[{activeBatteries}/{batteries}] CHG:{chargeBar}{charge:0}% FUEL:{fuelBar}{fuel:0}/{maxFuel:0}";
            Puts($"[DawnTerminal] Generated status: {result}");
            return result;
        }
        
        private string CreateProgressBar(float percentage, int length)
        {
            int filled = Mathf.RoundToInt(percentage * length);
            string bar = "";
            for (int i = 0; i < length; i++)
            {
                bar += i < filled ? "█" : "░";
            }
            return $"[{bar}]";
        }
        
        #endregion
        
        #region Authentication UI
        
        private void ShowRegistrationScreen(BasePlayer player)
        {
            var container = new CuiElementContainer();
            
            // Main background - dark amber theme
            container.Add(new CuiPanel
            {
                Image = { Color = "0.04 0.02 0 0.98" },
                RectTransform = { AnchorMin = "0 0", AnchorMax = "1 1" },
                CursorEnabled = true,
                KeyboardEnabled = true
            }, "Overlay", UI_REGISTER);
            
            // Top border
            // Top border (moved up slightly) and outer frame borders around the registration panel
            container.Add(new CuiPanel
            {
                Image = { Color = "1 0.67 0 0.9" },
                RectTransform = { AnchorMin = "0.28 0.78", AnchorMax = "0.72 0.783" }
            }, UI_REGISTER);

            // Left border
            container.Add(new CuiPanel
            {
                Image = { Color = "1 0.67 0 0.9" },
                RectTransform = { AnchorMin = "0.28 0.203", AnchorMax = "0.284 0.78" }
            }, UI_REGISTER);

            // Right border
            container.Add(new CuiPanel
            {
                Image = { Color = "1 0.67 0 0.9" },
                RectTransform = { AnchorMin = "0.716 0.203", AnchorMax = "0.72 0.78" }
            }, UI_REGISTER);

            // Bottom border
            container.Add(new CuiPanel
            {
                Image = { Color = "1 0.67 0 0.9" },
                RectTransform = { AnchorMin = "0.28 0.2", AnchorMax = "0.72 0.203" }
            }, UI_REGISTER);
            
            // Header
            container.Add(new CuiLabel
            {
                Text = {
                    Text = "DAWN SYSTEM - CL0NE_GENESIS PROTOCOL",
                    FontSize = 18,
                    Align = TextAnchor.MiddleCenter,
                    Color = "1 0.67 0 1"
                },
                RectTransform = { AnchorMin = "0.3 0.7", AnchorMax = "0.7 0.75" }
            }, UI_REGISTER);
            
            // Instructions
            container.Add(new CuiLabel
            {
                Text = {
                    Text = "ESTABLISH OBSERVATION CLEARANCE",
                    FontSize = 14,
                    Align = TextAnchor.MiddleCenter,
                    Color = "0 1 0 1"
                },
                RectTransform = { AnchorMin = "0.3 0.63", AnchorMax = "0.7 0.67" }
            }, UI_REGISTER);
            
            // Username label and instructions
            container.Add(new CuiLabel
            {
                Text = {
                    Text = "OBSERVATION DESIGNATION: Must match player name",
                    FontSize = 11,
                    Align = TextAnchor.MiddleLeft,
                    Color = "1 0.67 0 0.8"
                },
                RectTransform = { AnchorMin = "0.45 0.56", AnchorMax = "0.55 0.6" }
            }, UI_REGISTER);

            // Username input background (narrowed to match button width)
            container.Add(new CuiPanel
            {
                Image = { Color = "0.1 0.1 0.1 0.8" },
                RectTransform = { AnchorMin = "0.45 0.51", AnchorMax = "0.55 0.55" }
            }, UI_REGISTER, UI_REGISTER + ".UserBG");
            
            // Username input
            container.Add(new CuiElement
            {
                Parent = UI_REGISTER + ".UserBG",
                Components =
                {
                    new CuiInputFieldComponent
                    {
                        Align = TextAnchor.MiddleLeft,
                        CharsLimit = 32,
                        Command = "dawnauth.username ",
                        FontSize = 12,
                        Text = ""
                    },
                    new CuiRectTransformComponent { AnchorMin = "0 0", AnchorMax = "1 1" }
                }
            });
            
            // Password label and instructions
            container.Add(new CuiLabel
            {
                Text = {
                    Text = "PASSWORD: Must be 8 characters or less",
                    FontSize = 11,
                    Align = TextAnchor.MiddleLeft,
                    Color = "1 0.67 0 0.8"
                },
                RectTransform = { AnchorMin = "0.45 0.44", AnchorMax = "0.55 0.48" }
            }, UI_REGISTER);

            // Password input background (narrowed to match button width)
            container.Add(new CuiPanel
            {
                Image = { Color = "0.1 0.1 0.1 0.8" },
                RectTransform = { AnchorMin = "0.45 0.39", AnchorMax = "0.55 0.43" }
            }, UI_REGISTER, UI_REGISTER + ".PassBG");
            
            // Password input
            container.Add(new CuiElement
            {
                Parent = UI_REGISTER + ".PassBG",
                Components =
                {
                    new CuiInputFieldComponent
                    {
                        Align = TextAnchor.MiddleLeft,
                        CharsLimit = 8,
                        Command = "dawnauth.password ",
                        FontSize = 12,
                        IsPassword = true,
                        Text = ""
                    },
                    new CuiRectTransformComponent { AnchorMin = "0 0", AnchorMax = "1 1" }
                }
            });
            
            // Submit button
            // Submit button (width halved to match inputs)
            container.Add(new CuiButton
            {
                Button = {
                    Command = "dawnauth.register",
                    Color = "0.1 0.6 0.1 0.8"
                },
                RectTransform = { AnchorMin = "0.45 0.3", AnchorMax = "0.55 0.35" },
                Text = {
                    Text = "[ GENESIS CLONE ]",
                    FontSize = 12,
                    Align = TextAnchor.MiddleCenter,
                    Color = "0 0 0 1"
                }
            }, UI_REGISTER);
            
            // Error message placeholder
            container.Add(new CuiLabel
            {
                Text = {
                    Text = "",
                    FontSize = 11,
                    Align = TextAnchor.MiddleCenter,
                    Color = "1 0.23 0.23 1"
                },
                RectTransform = { AnchorMin = "0.3 0.23", AnchorMax = "0.7 0.27" }
            }, UI_REGISTER, UI_REGISTER + ".Error");
            
            CuiHelper.AddUi(player, container);
        }
        
        private void ShowLoginScreen(BasePlayer player, string errorMessage = "")
        {
            DestroyUI(player);
            
            var container = new CuiElementContainer();
            
            // Main background - dark amber theme
            container.Add(new CuiPanel
            {
                Image = { Color = "0.04 0.02 0 0.98" },
                RectTransform = { AnchorMin = "0 0", AnchorMax = "1 1" },
                CursorEnabled = true,
                KeyboardEnabled = true
            }, "Overlay", UI_LOGIN);
            
            // Top border (moved up slightly) and outer frame borders around the login panel
            container.Add(new CuiPanel
            {
                Image = { Color = "1 0.67 0 0.9" },
                RectTransform = { AnchorMin = "0.28 0.78", AnchorMax = "0.72 0.783" }
            }, UI_LOGIN);

            // Left border
            container.Add(new CuiPanel
            {
                Image = { Color = "1 0.67 0 0.9" },
                RectTransform = { AnchorMin = "0.28 0.203", AnchorMax = "0.284 0.78" }
            }, UI_LOGIN);

            // Right border
            container.Add(new CuiPanel
            {
                Image = { Color = "1 0.67 0 0.9" },
                RectTransform = { AnchorMin = "0.716 0.203", AnchorMax = "0.72 0.78" }
            }, UI_LOGIN);

            // Bottom border
            container.Add(new CuiPanel
            {
                Image = { Color = "1 0.67 0 0.9" },
                RectTransform = { AnchorMin = "0.28 0.2", AnchorMax = "0.72 0.203" }
            }, UI_LOGIN);
            
            // Header
            container.Add(new CuiLabel
            {
                Text = {
                    Text = "DAWN SYSTEM - ACCESS TERMINAL",
                    FontSize = 18,
                    Align = TextAnchor.MiddleCenter,
                    Color = "1 0.67 0 1"
                },
                RectTransform = { AnchorMin = "0.3 0.7", AnchorMax = "0.7 0.75" }
            }, UI_LOGIN);
            
            // Instructions
            container.Add(new CuiLabel
            {
                Text = {
                    Text = "ENTER YOUR CREDENTIALS",
                    FontSize = 14,
                    Align = TextAnchor.MiddleCenter,
                    Color = "0 1 0 1"
                },
                RectTransform = { AnchorMin = "0.3 0.63", AnchorMax = "0.7 0.67" }
            }, UI_LOGIN);
            
            // Username label
            container.Add(new CuiLabel
            {
                Text = {
                    Text = "USERNAME:",
                    FontSize = 11,
                    Align = TextAnchor.MiddleLeft,
                    Color = "1 0.67 0 0.8"
                },
                RectTransform = { AnchorMin = "0.45 0.56", AnchorMax = "0.55 0.6" }
            }, UI_LOGIN);

            // Username input background (narrowed to match button width)
            container.Add(new CuiPanel
            {
                Image = { Color = "0.1 0.1 0.1 0.8" },
                RectTransform = { AnchorMin = "0.45 0.51", AnchorMax = "0.55 0.55" }
            }, UI_LOGIN, UI_LOGIN + ".UserBG");
            
            // Username input
            container.Add(new CuiElement
            {
                Parent = UI_LOGIN + ".UserBG",
                Components =
                {
                    new CuiInputFieldComponent
                    {
                        Align = TextAnchor.MiddleLeft,
                        CharsLimit = 32,
                        Command = "dawnauth.username ",
                        FontSize = 12,
                        Text = ""
                    },
                    new CuiRectTransformComponent { AnchorMin = "0 0", AnchorMax = "1 1" }
                }
            });
            
            // Password label
            container.Add(new CuiLabel
            {
                Text = {
                    Text = "PASSWORD:",
                    FontSize = 11,
                    Align = TextAnchor.MiddleLeft,
                    Color = "1 0.67 0 0.8"
                },
                RectTransform = { AnchorMin = "0.45 0.44", AnchorMax = "0.55 0.48" }
            }, UI_LOGIN);

            // Password input background (narrowed to match button width)
            container.Add(new CuiPanel
            {
                Image = { Color = "0.1 0.1 0.1 0.8" },
                RectTransform = { AnchorMin = "0.45 0.39", AnchorMax = "0.55 0.43" }
            }, UI_LOGIN, UI_LOGIN + ".PassBG");
            
            // Password input
            container.Add(new CuiElement
            {
                Parent = UI_LOGIN + ".PassBG",
                Components =
                {
                    new CuiInputFieldComponent
                    {
                        Align = TextAnchor.MiddleLeft,
                        CharsLimit = 8,
                        Command = "dawnauth.password ",
                        FontSize = 12,
                        IsPassword = true,
                        Text = ""
                    },
                    new CuiRectTransformComponent { AnchorMin = "0 0", AnchorMax = "1 1" }
                }
            });
            
            // Submit button (width halved)
            container.Add(new CuiButton
            {
                Button = {
                    Command = "dawnauth.login",
                    Color = "0.1 0.6 0.1 0.8"
                },
                RectTransform = { AnchorMin = "0.45 0.3", AnchorMax = "0.55 0.35" },
                Text = {
                    Text = "[ LOGIN ]",
                    FontSize = 12,
                    Align = TextAnchor.MiddleCenter,
                    Color = "0 0 0 1"
                }
            }, UI_LOGIN);
            
            // Error message
            container.Add(new CuiLabel
            {
                Text = {
                    Text = errorMessage,
                    FontSize = 11,
                    Align = TextAnchor.MiddleCenter,
                    Color = "1 0.23 0.23 1"
                },
                RectTransform = { AnchorMin = "0.3 0.23", AnchorMax = "0.7 0.27" }
            }, UI_LOGIN, UI_LOGIN + ".Error");
            
            CuiHelper.AddUi(player, container);
        }
        
        private void ShowLaunchScreen(BasePlayer player)
        {
            DestroyUI(player);
            
            var container = new CuiElementContainer();
            
            // Main background - dark amber theme
            container.Add(new CuiPanel
            {
                Image = { Color = "0.04 0.02 0 0.98" },
                RectTransform = { AnchorMin = "0 0", AnchorMax = "1 1" },
                CursorEnabled = true
            }, "Overlay", UI_LOGIN);
            
            // Top border
            container.Add(new CuiPanel
            {
                Image = { Color = "1 0.67 0 0.8" },
                RectTransform = { AnchorMin = "0.3 0.7", AnchorMax = "0.7 0.703" }
            }, UI_LOGIN);
            
            // Header
            container.Add(new CuiLabel
            {
                Text = {
                    Text = "CL0NE IDENTITY VERIFIED",
                    FontSize = 18,
                    Align = TextAnchor.MiddleCenter,
                    Color = "0 1 0 1"
                },
                RectTransform = { AnchorMin = "0.3 0.65", AnchorMax = "0.7 0.7" }
            }, UI_LOGIN);
            
            // Welcome message
            container.Add(new CuiLabel
            {
                Text = {
                    Text = $"Observation Unit: <color=#FFFFFF>{player.displayName}</color>",
                    FontSize = 16,
                    Align = TextAnchor.MiddleCenter,
                    Color = "1 0.67 0 1"
                },
                RectTransform = { AnchorMin = "0.3 0.58", AnchorMax = "0.7 0.62" }
            }, UI_LOGIN);
            
            // Access granted message
            container.Add(new CuiLabel
            {
                Text = {
                    Text = "[STATUS] Observer uplink established",
                    FontSize = 12,
                    Align = TextAnchor.MiddleCenter,
                    Color = "1 0.67 0 0.8"
                },
                RectTransform = { AnchorMin = "0.3 0.53", AnchorMax = "0.7 0.57" }
            }, UI_LOGIN);
            
            // Warning text 1
            container.Add(new CuiLabel
            {
                Text = {
                    Text = "[WARNING] Mapping network requires power feed",
                    FontSize = 11,
                    Align = TextAnchor.MiddleCenter,
                    Color = "1 0.67 0 0.9"
                },
                RectTransform = { AnchorMin = "0.3 0.48", AnchorMax = "0.7 0.52" }
            }, UI_LOGIN);
            
            // Warning text 2 background (red)
            container.Add(new CuiPanel
            {
                Image = { Color = "0.6 0 0 0.9" },
                RectTransform = { AnchorMin = "0.35 0.44", AnchorMax = "0.65 0.48" }
            }, UI_LOGIN, UI_LOGIN + ".WarningBG");
            
            // Warning text 2
            container.Add(new CuiLabel
            {
                Text = {
                    Text = "Maintain generators or lose uplink",
                    FontSize = 10,
                    Align = TextAnchor.MiddleCenter,
                    Color = "1 1 1 1"
                },
                RectTransform = { AnchorMin = "0 0", AnchorMax = "1 1" }
            }, UI_LOGIN + ".WarningBG");
            
            // Launch button border (yellow)
            container.Add(new CuiPanel
            {
                Image = { Color = "1 0.67 0 0.9" },
                RectTransform = { AnchorMin = "0.348 0.348", AnchorMax = "0.652 0.402" }
            }, UI_LOGIN);
            
            // Launch button
            container.Add(new CuiButton
            {
                Button = {
                    Command = "dawnauth.launch",
                    Color = "0.6 0 0 0.95"
                },
                RectTransform = { AnchorMin = "0.35 0.35", AnchorMax = "0.65 0.4" },
                Text = {
                    Text = "[ LAUNCH DAWN SYS BOOT SEQUENCE ]",
                    FontSize = 13,
                    Align = TextAnchor.MiddleCenter,
                    Color = "1 1 1 1"
                }
            }, UI_LOGIN);
            
            CuiHelper.AddUi(player, container);
        }
        
        #endregion
        
        #region UI Creation
        
        private void ShowBootSequence(BasePlayer player)
        {
            var container = new CuiElementContainer();
            
            // Main background panel - full screen black
            container.Add(new CuiPanel
            {
                Image = { Color = "0 0 0 1" },
                RectTransform = { AnchorMin = "0 0", AnchorMax = "1 1" },
                CursorEnabled = true
            }, "Overlay", UI_BOOT);
            
            // DAWN SYS title
            container.Add(new CuiLabel
            {
                Text = {
                    Text = "DAWN SYS",
                    FontSize = 48,
                    Align = TextAnchor.MiddleCenter,
                    Color = "0 1 0 1"
                },
                RectTransform = { AnchorMin = "0.3 0.7", AnchorMax = "0.7 0.85" }
            }, UI_BOOT);
            
            // Version subtitle
            container.Add(new CuiLabel
            {
                Text = {
                    Text = "v2.6.11 // RUST SECTOR GRID",
                    FontSize = 12,
                    Align = TextAnchor.MiddleCenter,
                    Color = "1 0.67 0 0.8"
                },
                RectTransform = { AnchorMin = "0.35 0.65", AnchorMax = "0.65 0.7" }
            }, UI_BOOT);
            
            // Boot sequence text
            var bootLines = new List<string>
            {
                "[DAWN] Boot sequence initiated.",
                "[DAWN] Syncing SATCOM relay…",
                "[DAWN] ERROR: Unable to reach uplink node 7A",
                "[DAWN] Atmospheric scan started…",
                "[DAWN] Rift signal checksum passed.",
                "[DAWN] ERROR: VFC pattern unresolved — retrying...",
                "[DAWN] Lumen telemetry nominal.",
                "[DAWN] Signal from Node-21 accepted.",
                "[DAWN] Clone signature detected: " + player.displayName,
                "[DAWN] Awaiting operator authentication…"
            };
            
            float startY = 0.55f;
            float lineHeight = 0.03f;
            
            for (int i = 0; i < bootLines.Count; i++)
            {
                var line = bootLines[i];
                var color = line.Contains("ERROR") ? "1 0.23 0.23 1" : "1 0.67 0 1";
                
                container.Add(new CuiLabel
                {
                    Text = {
                        Text = line,
                        FontSize = 11,
                        Align = TextAnchor.MiddleLeft,
                        Color = color,
                        Font = "robotocondensed-regular.ttf"
                    },
                    RectTransform = { 
                        AnchorMin = $"0.25 {startY - (i * lineHeight)}", 
                        AnchorMax = $"0.75 {startY - (i * lineHeight) + lineHeight}" 
                    }
                }, UI_BOOT);
            }
            
            // Continue button
            container.Add(new CuiButton
            {
                Button = {
                    Command = "dawnterminal.enter",
                    Color = "0 0 0 0.8"
                },
                RectTransform = { AnchorMin = "0.42 0.15", AnchorMax = "0.58 0.2" },
                Text = {
                    Text = "[ PRESS TO CONTINUE ]",
                    FontSize = 12,
                    Align = TextAnchor.MiddleCenter,
                    Color = "1 0.67 0 1"
                }
            }, UI_BOOT);
            
            CuiHelper.AddUi(player, container);
            
            // Play sound effect
            Effect.server.Run("assets/prefabs/misc/xmas/advent_calendar/effects/open_advent.prefab", player.transform.position);
        }
        
        private void ShowMainTerminal(BasePlayer player, int pageIndex = 0)
        {
            DestroyUI(player);
            
            // Clean up any existing typing timer
            if (typingTimers.ContainsKey(player.userID))
            {
                typingTimers[player.userID]?.Destroy();
                typingTimers.Remove(player.userID);
            }
            
            if (pageIndex < 0) pageIndex = 0;
            if (pageIndex >= pages.Count) pageIndex = pages.Count - 1;
            
            playerPages[player.userID] = pageIndex;
            var page = pages[pageIndex];
            
            // If it's the first page, use typing animation
            if (pageIndex == 0)
            {
                ShowMainTerminalWithTyping(player, page, pageIndex);
            }
            // If it's page 2 (hub), show special hub menu
            else if (pageIndex == 1)
            {
                ShowTerminalHub(player, page);
            }
            else
            {
                ShowMainTerminalStatic(player, page, pageIndex);
            }
        }
        
        private void ShowMainTerminalWithTyping(BasePlayer player, TerminalPage page, int pageIndex)
        {
            typingLineIndex[player.userID] = 0;
            
            var container = new CuiElementContainer();
            
            // Main background - amber/green theme
            container.Add(new CuiPanel
            {
                Image = { Color = "0.04 0.02 0 0.98" },
                RectTransform = { AnchorMin = "0 0", AnchorMax = "1 1" },
                CursorEnabled = true
            }, "Overlay", UI_MAIN);
            
            // CRT scanline overlay effect
            container.Add(new CuiPanel
            {
                Image = { 
                    Color = "1 1 1 0.02",
                    Material = "assets/content/ui/uibackgroundblur-ingamemenu.mat"
                },
                RectTransform = { AnchorMin = "0 0", AnchorMax = "1 1" }
            }, UI_MAIN);
            
            // Top border
            container.Add(new CuiPanel
            {
                Image = { Color = "1 0.67 0 0.8" },
                RectTransform = { AnchorMin = "0.05 0.92", AnchorMax = "0.95 0.925" }
            }, UI_MAIN);
            
            // Header - DAWN SYS
            container.Add(new CuiLabel
            {
                Text = {
                    Text = "DAWN SYS",
                    FontSize = 32,
                    Align = TextAnchor.MiddleCenter,
                    Color = "0 1 0 1"
                },
                RectTransform = { AnchorMin = "0.1 0.88", AnchorMax = "0.9 0.92" }
            }, UI_MAIN);
            
            // Power Grid Status (replaces version subtitle)
            string powerStatus = GetPowerGridStatus(player);
            container.Add(new CuiLabel
            {
                Text = {
                    Text = powerStatus,
                    FontSize = 13,
                    Align = TextAnchor.MiddleCenter,
                    Color = "0 1 0 1"
                },
                RectTransform = { AnchorMin = "0.1 0.84", AnchorMax = "0.9 0.87" }
            }, UI_MAIN);
            
            // Page title
            container.Add(new CuiLabel
            {
                Text = {
                    Text = $">> {page.Title.ToUpper()}",
                    FontSize = 16,
                    Align = TextAnchor.MiddleLeft,
                    Color = "1 0.67 0 1"
                },
                RectTransform = { AnchorMin = "0.08 0.79", AnchorMax = "0.92 0.84" }
            }, UI_MAIN);
            
            // Content area with border
            container.Add(new CuiPanel
            {
                Image = { Color = "0.07 0.07 0.07 0.9" },
                RectTransform = { AnchorMin = "0.08 0.15", AnchorMax = "0.92 0.77" }
            }, UI_MAIN, UI_MAIN + ".ContentBG");
            
            // Border for content
            container.Add(new CuiPanel
            {
                Image = { Color = "1 0.67 0 0.5" },
                RectTransform = { AnchorMin = "0.078 0.148", AnchorMax = "0.922 0.152" }
            }, UI_MAIN);
            container.Add(new CuiPanel
            {
                Image = { Color = "1 0.67 0 0.5" },
                RectTransform = { AnchorMin = "0.078 0.768", AnchorMax = "0.922 0.772" }
            }, UI_MAIN);
            container.Add(new CuiPanel
            {
                Image = { Color = "1 0.67 0 0.5" },
                RectTransform = { AnchorMin = "0.078 0.15", AnchorMax = "0.082 0.77" }
            }, UI_MAIN);
            container.Add(new CuiPanel
            {
                Image = { Color = "1 0.67 0 0.5" },
                RectTransform = { AnchorMin = "0.918 0.15", AnchorMax = "0.922 0.77" }
            }, UI_MAIN);
            
            // Navigation buttons
            // Close button (bottom left)
            container.Add(new CuiButton
            {
                Button = {
                    Command = "dawnterminal.close",
                    Color = "0.5 0 0 0.8"
                },
                RectTransform = { AnchorMin = "0.08 0.08", AnchorMax = "0.28 0.13" },
                Text = {
                    Text = "[ DISCONNECT ]",
                    FontSize = 12,
                    Align = TextAnchor.MiddleCenter,
                    Color = "1 1 1 1"
                }
            }, UI_MAIN);
            
            // Footer with player info and power stats
            string footerText = $"[SYSTEM] Clone ID: {player.userID} | Coordinates: {player.transform.position}";
            
            // Try to get DawnSys power stats
            if (DawnSys != null)
            {
                var stats = DawnSys.Call("API_GetPlayerPowerStats", player.userID) as Dictionary<string, object>;
                if (stats != null)
                {
                    int generators = Convert.ToInt32(stats["generators"]);
                    int batteries = Convert.ToInt32(stats["batteries"]);
                    int activeBatteries = Convert.ToInt32(stats["activeBatteries"]);
                    float charge = Convert.ToSingle(stats["charge"]);
                    float fuel = Convert.ToSingle(stats["fuelStored"]);
                    
                    footerText = $"[POWER GRID] [{generators}] generators // [{activeBatteries}/{batteries}] batteries @ [{charge:0}]% // Fuel: [{fuel:0}] units";
                }
            }
            
            container.Add(new CuiLabel
            {
                Text = {
                    Text = footerText,
                    FontSize = 9,
                    Align = TextAnchor.MiddleLeft,
                    Color = "1 0.67 0 0.6"
                },
                RectTransform = { AnchorMin = "0.08 0.03", AnchorMax = "0.92 0.06" }
            }, UI_MAIN);
            
            CuiHelper.AddUi(player, container);
            
            // Start typing animation - add +1 to allow the final callback to show buttons
            typingTimers[player.userID] = timer.Repeat(0.08f, page.Content.Count + 1, () =>
            {
                if (player == null || !player.IsConnected) return;
                
                TypeNextLine(player, page);
            });
        }
        
        private void TypeNextLine(BasePlayer player, TerminalPage page)
        {
            if (!typingLineIndex.ContainsKey(player.userID)) return;
            
            int lineIndex = typingLineIndex[player.userID];
            
            // Check if typing is complete FIRST, before any other checks
            if (lineIndex >= page.Content.Count)
            {
                // Typing complete - show loading bar, then navigation buttons
                ShowLoadingBar(player);
                typingLineIndex[player.userID]++; // Increment to prevent re-triggering
                return;
            }
            
            var line = page.Content[lineIndex];
            float contentY = 0.72f - (lineIndex * 0.03f);
            
            if (contentY < 0.2f) 
            {
                // Still increment even if we can't display
                typingLineIndex[player.userID]++;
                return;
            }
            
            // Default color coding (will be overridden by <color> tags if present)
            var defaultColor = "1 0.67 0 0.7";
            if (line.Contains("<color=")) 
            {
                // Line has color tags - use default amber for base
                defaultColor = "1 0.67 0 0.8";
            }
            else if (line.StartsWith("[")) 
            {
                defaultColor = "1 0.67 0 1";
            }
            else if (line.StartsWith("=")) 
            {
                defaultColor = "1 0.67 0 0.5";
            }
            
            var fontSize = line.StartsWith("=") ? 13 : 12;
            
            var container = new CuiElementContainer();
            container.Add(new CuiLabel
            {
                Text = {
                    Text = line,
                    FontSize = fontSize,
                    Align = TextAnchor.UpperLeft,
                    Color = defaultColor,
                    Font = "robotocondensed-regular.ttf"
                },
                RectTransform = { 
                    AnchorMin = $"0.12 {contentY}", 
                    AnchorMax = $"0.88 {contentY + 0.03f}" 
                }
            }, UI_MAIN + ".ContentBG", UI_MAIN + ".Line" + lineIndex);
            
            CuiHelper.AddUi(player, container);
            
            // Play subtle beep sound for certain lines
            if (line.Contains("[ALERT]") || line.Contains("[WARNING]"))
            {
                Effect.server.Run("assets/bundled/prefabs/fx/notice/item.select.fx.prefab", player.transform.position);
            }
            
            typingLineIndex[player.userID]++;
        }
        
        private void ShowLoadingBar(BasePlayer player)
        {
            var container = new CuiElementContainer();
            
            // Loading text above bar
            container.Add(new CuiLabel
            {
                Text = {
                    Text = "Initializing terminal access...",
                    FontSize = 10,
                    Align = TextAnchor.MiddleLeft,
                    Color = "1 0.67 0 0.7",
                    Font = "robotocondensed-regular.ttf"
                },
                RectTransform = { AnchorMin = "0.12 0.095", AnchorMax = "0.88 0.115" }
            }, UI_MAIN + ".ContentBG", UI_MAIN + ".LoadingText");
            
            // Loading bar background
            container.Add(new CuiPanel
            {
                Image = { Color = "0.1 0.1 0.1 0.8" },
                RectTransform = { AnchorMin = "0.12 0.07", AnchorMax = "0.88 0.09" }
            }, UI_MAIN + ".ContentBG", UI_MAIN + ".LoadingBarBG");
            
            // Loading bar border (green)
            container.Add(new CuiPanel
            {
                Image = { Color = "1 0.67 0 0.5" },
                RectTransform = { AnchorMin = "0.119 0.069", AnchorMax = "0.881 0.091" }
            }, UI_MAIN + ".ContentBG");
            
            // Draining bar background (red) - directly below green bar
            container.Add(new CuiPanel
            {
                Image = { Color = "0 0 0 0.7" },
                RectTransform = { AnchorMin = "0.12 0.045", AnchorMax = "0.88 0.065" }
            }, UI_MAIN + ".ContentBG", UI_MAIN + ".DrainingBarBG");
            
            // Draining bar border (red)
            container.Add(new CuiPanel
            {
                Image = { Color = "1 0 0 0.5" },
                RectTransform = { AnchorMin = "0.119 0.044", AnchorMax = "0.881 0.066" }
            }, UI_MAIN + ".ContentBG");
            
            // Initial full red bar
            container.Add(new CuiPanel
            {
                Image = { Color = "1 0 0 0.8" },
                RectTransform = { 
                    AnchorMin = "0.001 0.001", 
                    AnchorMax = "0.998 0.999" 
                }
            }, UI_MAIN + ".DrainingBarBG", UI_MAIN + ".DrainingFill");
            
            CuiHelper.AddUi(player, container);
            
            // Animate loading bar fill AND draining bar
            int tickCount = 0;
            const int maxTicks = 50; // Doubled for slower loading (5 seconds total)
            
            Timer loadingTimer = timer.Repeat(0.1f, maxTicks + 1, () =>
            {
                if (player == null || !player.IsConnected) return;
                
                tickCount++;
                float progress = (float)tickCount / maxTicks;
                if (progress > 1f) progress = 1f;
                
                // Calculate drain progress - red bar should shrink from right side
                float remainingRed = 1f - (progress * 1.15f); // Drains 15% faster
                if (remainingRed < 0f) remainingRed = 0f;
                
                // Remove old fills
                CuiHelper.DestroyUi(player, UI_MAIN + ".LoadingFill");
                CuiHelper.DestroyUi(player, UI_MAIN + ".DrainingFill");
                
                // Add new green fill (left to right)
                var fillContainer = new CuiElementContainer();
                fillContainer.Add(new CuiPanel
                {
                    Image = { Color = "0 1 0.5 0.8" },
                    RectTransform = { 
                        AnchorMin = "0.001 0.001", 
                        AnchorMax = $"{progress * 0.998} 0.999" 
                    }
                }, UI_MAIN + ".LoadingBarBG", UI_MAIN + ".LoadingFill");
                
                // Add new red fill (fills from left but width decreases)
                if (remainingRed > 0f)
                {
                    fillContainer.Add(new CuiPanel
                    {
                        Image = { Color = "1 0 0 0.8" },
                        RectTransform = { 
                            AnchorMin = "0.001 0.001", 
                            AnchorMax = $"{remainingRed * 0.998} 0.999" 
                        }
                    }, UI_MAIN + ".DrainingBarBG", UI_MAIN + ".DrainingFill");
                }
                
                CuiHelper.AddUi(player, fillContainer);
                
                // When complete, show the prompt
                if (tickCount >= maxTicks)
                {
                    timer.Once(0.5f, () =>
                    {
                        if (player != null && player.IsConnected)
                        {
                            ShowNavigationButtons(player, 0);
                        }
                    });
                }
            });
        }
        
        private void ShowNavigationButtons(BasePlayer player, int pageIndex)
        {
            var container = new CuiElementContainer();
            
            // Center screen overlay for "Enter Terminal" prompt (first page only)
            if (pageIndex == 0)
            {
                // Red border around the button (add first, so it's behind)
                container.Add(new CuiPanel
                {
                    Image = { Color = "0.8 0 0 0.9" },
                    RectTransform = { AnchorMin = "0.377 0.457", AnchorMax = "0.623 0.543" }
                }, UI_MAIN, UI_MAIN + ".PromptBorder");
                
                // Smaller centered enter button with glow effect and contrasting text
                container.Add(new CuiButton
                {
                    Button = {
                        Command = "dawnterminal.page 1",
                        Color = "0.1 0.7 0.1 0.95"
                    },
                    RectTransform = { AnchorMin = "0.38 0.46", AnchorMax = "0.62 0.54" },
                    Text = {
                        Text = "[ ENTER TERMINAL ]",
                        FontSize = 14,
                        Align = TextAnchor.MiddleCenter,
                        Color = "0 0 0 1"  // Black text for contrast against green button
                    }
                }, UI_MAIN, UI_MAIN + ".EnterBtn");
            }
            
            CuiHelper.AddUi(player, container);
            
            // Play completion sound
            Effect.server.Run("assets/bundled/prefabs/fx/notice/loot.drag.grab.fx.prefab", player.transform.position);
        }
        
        private void ShowTerminalHub(BasePlayer player, TerminalPage page)
        {
            var container = new CuiElementContainer();
            
            // Main background - amber/green theme
            container.Add(new CuiPanel
            {
                Image = { Color = "0.04 0.02 0 0.98" },
                RectTransform = { AnchorMin = "0 0", AnchorMax = "1 1" },
                CursorEnabled = true
            }, "Overlay", UI_MAIN);
            
            // CRT scanline overlay effect
            container.Add(new CuiPanel
            {
                Image = { 
                    Color = "1 1 1 0.02",
                    Material = "assets/content/ui/uibackgroundblur-ingamemenu.mat"
                },
                RectTransform = { AnchorMin = "0 0", AnchorMax = "1 1" }
            }, UI_MAIN);
            
            // Top border
            container.Add(new CuiPanel
            {
                Image = { Color = "1 0.67 0 0.8" },
                RectTransform = { AnchorMin = "0.05 0.92", AnchorMax = "0.95 0.925" }
            }, UI_MAIN);
            
            // Header
            container.Add(new CuiLabel
            {
                Text = {
                    Text = "═══ DAWN SYSTEM TERMINAL ═══",
                    FontSize = 20,
                    Align = TextAnchor.MiddleCenter,
                    Color = "1 0.67 0 1"
                },
                RectTransform = { AnchorMin = "0.1 0.88", AnchorMax = "0.9 0.92" }
            }, UI_MAIN);
            
            // Power Grid Status
            string powerStatusHub = GetPowerGridStatus(player);
            container.Add(new CuiLabel
            {
                Text = {
                    Text = powerStatusHub,
                    FontSize = 13,
                    Align = TextAnchor.MiddleCenter,
                    Color = "0 1 0 1"
                },
                RectTransform = { AnchorMin = "0.1 0.84", AnchorMax = "0.9 0.87" }
            }, UI_MAIN);
            
            // Page title
            container.Add(new CuiLabel
            {
                Text = {
                    Text = $">> {page.Title.ToUpper()}",
                    FontSize = 16,
                    Align = TextAnchor.MiddleLeft,
                    Color = "1 0.67 0 1"
                },
                RectTransform = { AnchorMin = "0.08 0.82", AnchorMax = "0.92 0.87" }
            }, UI_MAIN);
            
            // Content area with border
            container.Add(new CuiPanel
            {
                Image = { Color = "0.07 0.07 0.07 0.9" },
                RectTransform = { AnchorMin = "0.08 0.15", AnchorMax = "0.92 0.8" }
            }, UI_MAIN, UI_MAIN + ".ContentBG");
            
            // Border for content
            container.Add(new CuiPanel
            {
                Image = { Color = "1 0.67 0 0.5" },
                RectTransform = { AnchorMin = "0.078 0.148", AnchorMax = "0.922 0.152" }
            }, UI_MAIN);
            container.Add(new CuiPanel
            {
                Image = { Color = "1 0.67 0 0.5" },
                RectTransform = { AnchorMin = "0.078 0.798", AnchorMax = "0.922 0.802" }
            }, UI_MAIN);
            container.Add(new CuiPanel
            {
                Image = { Color = "1 0.67 0 0.5" },
                RectTransform = { AnchorMin = "0.078 0.15", AnchorMax = "0.082 0.8" }
            }, UI_MAIN);
            container.Add(new CuiPanel
            {
                Image = { Color = "1 0.67 0 0.5" },
                RectTransform = { AnchorMin = "0.918 0.15", AnchorMax = "0.922 0.8" }
            }, UI_MAIN);
            
            // Display header text
            container.Add(new CuiLabel
            {
                Text = {
                    Text = "<color=lime>[DAWN SYSTEM ONLINE]</color>\n\nSelect terminal subsystem to access:",
                    FontSize = 14,
                    Align = TextAnchor.UpperCenter,
                    Color = "1 0.67 0 0.8",
                    Font = "robotocondensed-regular.ttf"
                },
                RectTransform = { AnchorMin = "0.12 0.65", AnchorMax = "0.88 0.75" }
            }, UI_MAIN + ".ContentBG");
            
            // Hub Navigation Buttons - 2x2 grid with color coding
            // Button 1: Operational Protocols (top left) - Page 2 - AMBER/ORANGE
            container.Add(new CuiPanel
            {
                Image = { Color = "1 1 1 0.3" },
                RectTransform = { AnchorMin = "0.148 0.448", AnchorMax = "0.482 0.582" }
            }, UI_MAIN + ".ContentBG", UI_MAIN + ".Btn1Outline");
            
            container.Add(new CuiButton
            {
                Button = {
                    Command = "dawnterminal.page 2",
                    Color = "0.3 0.15 0 0.9"
                },
                RectTransform = { AnchorMin = "0.15 0.45", AnchorMax = "0.48 0.58" },
                Text = {
                    Text = "[ OPERATIONAL PROTOCOLS ]\n\nRules • Commands • Guidelines",
                    FontSize = 12,
                    Align = TextAnchor.MiddleCenter,
                    Color = "1 0.67 0 1"
                }
            }, UI_MAIN + ".ContentBG");
            
            // Button 2: Starfall Archives (top right) - Page 3 - CYAN/BLUE
            container.Add(new CuiPanel
            {
                Image = { Color = "1 1 1 0.3" },
                RectTransform = { AnchorMin = "0.518 0.448", AnchorMax = "0.852 0.582" }
            }, UI_MAIN + ".ContentBG", UI_MAIN + ".Btn2Outline");
            
            container.Add(new CuiButton
            {
                Button = {
                    Command = "dawnterminal.page 3",
                    Color = "0 0.15 0.3 0.9"
                },
                RectTransform = { AnchorMin = "0.52 0.45", AnchorMax = "0.85 0.58" },
                Text = {
                    Text = "[ STARFALL ARCHIVES ]\n\nHistorical Records • Event Logs",
                    FontSize = 12,
                    Align = TextAnchor.MiddleCenter,
                    Color = "0 0.8 1 1"
                }
            }, UI_MAIN + ".ContentBG");
            
            // Button 3: Core Directives (middle left) - Page 4 - PURPLE/MAGENTA
            container.Add(new CuiPanel
            {
                Image = { Color = "1 1 1 0.3" },
                RectTransform = { AnchorMin = "0.148 0.298", AnchorMax = "0.482 0.432" }
            }, UI_MAIN + ".ContentBG", UI_MAIN + ".Btn3Outline");
            
            container.Add(new CuiButton
            {
                Button = {
                    Command = "dawnterminal.page 4",
                    Color = "0.25 0 0.3 0.9"
                },
                RectTransform = { AnchorMin = "0.15 0.3", AnchorMax = "0.48 0.43" },
                Text = {
                    Text = "[ CORE DIRECTIVES ]\n\nThe Three Laws • Operating Code",
                    FontSize = 12,
                    Align = TextAnchor.MiddleCenter,
                    Color = "1 1 1 1"
                }
            }, UI_MAIN + ".ContentBG");
            
            // Button 4: Clone Protocol (middle right) - Page 5 - GREEN
            container.Add(new CuiPanel
            {
                Image = { Color = "1 1 1 0.3" },
                RectTransform = { AnchorMin = "0.518 0.298", AnchorMax = "0.852 0.432" }
            }, UI_MAIN + ".ContentBG", UI_MAIN + ".Btn4Outline");
            
            container.Add(new CuiButton
            {
                Button = {
                    Command = "dawnterminal.page 5",
                    Color = "0 0.25 0.1 0.9"
                },
                RectTransform = { AnchorMin = "0.52 0.3", AnchorMax = "0.85 0.43" },
                Text = {
                    Text = "[ CLONE PROTOCOL ]\n\nBiological Assets • Genesis Files",
                    FontSize = 12,
                    Align = TextAnchor.MiddleCenter,
                    Color = "0.4 1 0.5 1"
                }
            }, UI_MAIN + ".ContentBG");
            
            // Button 5: Active Objectives (bottom left) - Page 6 - YELLOW/GOLD
            container.Add(new CuiPanel
            {
                Image = { Color = "1 1 1 0.3" },
                RectTransform = { AnchorMin = "0.148 0.178", AnchorMax = "0.482 0.282" }
            }, UI_MAIN + ".ContentBG", UI_MAIN + ".Btn5Outline");
            
            container.Add(new CuiButton
            {
                Button = {
                    Command = "dawnterminal.page 6",
                    Color = "0.3 0.25 0 0.9"
                },
                RectTransform = { AnchorMin = "0.15 0.18", AnchorMax = "0.48 0.28" },
                Text = {
                    Text = "[ ACTIVE OBJECTIVES ]\n\nField Operations • Mission Parameters",
                    FontSize = 12,
                    Align = TextAnchor.MiddleCenter,
                    Color = "1 0.9 0.3 1"
                }
            }, UI_MAIN + ".ContentBG");
            
            // Button 6: Power Grid (bottom right) - Page 7 - RED
            container.Add(new CuiPanel
            {
                Image = { Color = "1 1 1 0.3" },
                RectTransform = { AnchorMin = "0.518 0.178", AnchorMax = "0.852 0.282" }
            }, UI_MAIN + ".ContentBG", UI_MAIN + ".Btn6Outline");
            
            container.Add(new CuiButton
            {
                Button = {
                    Command = "dawnterminal.page 7",
                    Color = "0.4 0 0 0.9"
                },
                RectTransform = { AnchorMin = "0.52 0.18", AnchorMax = "0.85 0.28" },
                Text = {
                    Text = "[ POWER GRID ]\n\nDawnSys • Energy Management",
                    FontSize = 12,
                    Align = TextAnchor.MiddleCenter,
                    Color = "1 1 1 1"
                }
            }, UI_MAIN + ".ContentBG");
            
            // Close button
            container.Add(new CuiButton
            {
                Button = {
                    Command = "dawnterminal.close",
                    Color = "0.5 0 0 0.8"
                },
                RectTransform = { AnchorMin = "0.4 0.08", AnchorMax = "0.6 0.12" },
                Text = {
                    Text = "[ DISCONNECT ]",
                    FontSize = 12,
                    Align = TextAnchor.MiddleCenter,
                    Color = "1 1 1 1"
                }
            }, UI_MAIN);
            
            // Footer with player info
            container.Add(new CuiLabel
            {
                Text = {
                    Text = $"[SYSTEM] Clone ID: {player.userID} | Coordinates: {player.transform.position}",
                    FontSize = 9,
                    Align = TextAnchor.MiddleLeft,
                    Color = "1 0.67 0 0.6"
                },
                RectTransform = { AnchorMin = "0.08 0.03", AnchorMax = "0.92 0.06" }
            }, UI_MAIN);
            
            CuiHelper.AddUi(player, container);
        }
        
        private void ShowMainTerminalStatic(BasePlayer player, TerminalPage page, int pageIndex)
        {
            var container = new CuiElementContainer();
            
            // Main background - amber/green theme
            container.Add(new CuiPanel
            {
                Image = { Color = "0.04 0.02 0 0.98" }, // Dark amber background
                RectTransform = { AnchorMin = "0 0", AnchorMax = "1 1" },
                CursorEnabled = true
            }, "Overlay", UI_MAIN);
            
            // CRT scanline overlay effect
            container.Add(new CuiPanel
            {
                Image = { 
                    Color = "1 1 1 0.02",
                    Material = "assets/content/ui/uibackgroundblur-ingamemenu.mat"
                },
                RectTransform = { AnchorMin = "0 0", AnchorMax = "1 1" }
            }, UI_MAIN);
            
            // Top border
            container.Add(new CuiPanel
            {
                Image = { Color = "1 0.67 0 0.8" },
                RectTransform = { AnchorMin = "0.05 0.92", AnchorMax = "0.95 0.925" }
            }, UI_MAIN);
            
            // Header
            container.Add(new CuiLabel
            {
                Text = {
                    Text = "═══ DAWN SYSTEM TERMINAL ═══",
                    FontSize = 20,
                    Align = TextAnchor.MiddleCenter,
                    Color = "1 0.67 0 1"
                },
                RectTransform = { AnchorMin = "0.1 0.88", AnchorMax = "0.9 0.92" }
            }, UI_MAIN);
            
            // Power Grid Status
            string powerStatusStatic = GetPowerGridStatus(player);
            container.Add(new CuiLabel
            {
                Text = {
                    Text = powerStatusStatic,
                    FontSize = 13,
                    Align = TextAnchor.MiddleCenter,
                    Color = "0 1 0 1"
                },
                RectTransform = { AnchorMin = "0.1 0.84", AnchorMax = "0.9 0.87" }
            }, UI_MAIN);
            
            // Page title
            container.Add(new CuiLabel
            {
                Text = {
                    Text = $">> {page.Title.ToUpper()}",
                    FontSize = 16,
                    Align = TextAnchor.MiddleLeft,
                    Color = "1 0.67 0 1"
                },
                RectTransform = { AnchorMin = "0.08 0.82", AnchorMax = "0.92 0.87" }
            }, UI_MAIN);
            
            // Content area with border
            container.Add(new CuiPanel
            {
                Image = { Color = "0.07 0.07 0.07 0.9" },
                RectTransform = { AnchorMin = "0.08 0.15", AnchorMax = "0.92 0.8" }
            }, UI_MAIN, UI_MAIN + ".ContentBG");
            
            // Border for content
            container.Add(new CuiPanel
            {
                Image = { Color = "1 0.67 0 0.5" },
                RectTransform = { AnchorMin = "0.078 0.148", AnchorMax = "0.922 0.152" }
            }, UI_MAIN);
            container.Add(new CuiPanel
            {
                Image = { Color = "1 0.67 0 0.5" },
                RectTransform = { AnchorMin = "0.078 0.798", AnchorMax = "0.922 0.802" }
            }, UI_MAIN);
            container.Add(new CuiPanel
            {
                Image = { Color = "1 0.67 0 0.5" },
                RectTransform = { AnchorMin = "0.078 0.15", AnchorMax = "0.082 0.8" }
            }, UI_MAIN);
            container.Add(new CuiPanel
            {
                Image = { Color = "1 0.67 0 0.5" },
                RectTransform = { AnchorMin = "0.918 0.15", AnchorMax = "0.922 0.8" }
            }, UI_MAIN);
            
            // Check if this is the Operational Protocols page (page 2) - use two-column layout
            if (pageIndex == 2)
            {
                ShowProtocolsPage(player, page, container);
            }
            else
            {
                // Content text - improved positioning and spacing
                float contentY = 0.75f;
                float contentLineHeight = 0.0235f;  // Smaller line height to fit more content
                
                int lineCounter = 0;
                foreach (var line in page.Content)
                {
                    // Color coding with support for color tags
                    var defaultColor = "1 0.67 0 0.7";
                    if (line.Contains("<color="))
                    {
                        defaultColor = "1 0.67 0 0.8";
                    }
                    else if (line.StartsWith("["))
                    {
                        defaultColor = "1 0.67 0 1";
                    }
                    else if (line.StartsWith("="))
                    {
                        defaultColor = "1 0.67 0 0.5";
                    }
                    
                    var fontSize = line.StartsWith("=") ? 11 : 10;  // Smaller font to fit more
                    var alignment = TextAnchor.UpperLeft;
                    
                    container.Add(new CuiLabel
                    {
                        Text = {
                            Text = line,
                            FontSize = fontSize,
                            Align = alignment,
                            Color = defaultColor,
                            Font = "robotocondensed-regular.ttf"
                        },
                        RectTransform = { 
                            AnchorMin = $"0.12 {contentY}", 
                            AnchorMax = $"0.88 {contentY + contentLineHeight}" 
                        }
                    }, UI_MAIN + ".ContentBG", UI_MAIN + ".StaticLine" + lineCounter);
                    
                    contentY -= contentLineHeight;
                    lineCounter++;
                    
                    if (contentY < 0.05f) break; // Lower threshold to show more content
                }
            }
            
            // Navigation buttons for detail pages
            // Return to Hub button
            container.Add(new CuiButton
            {
                Button = {
                    Command = "dawnterminal.page 1",
                    Color = "0.1 0.4 0.1 0.8"
                },
                RectTransform = { AnchorMin = "0.08 0.08", AnchorMax = "0.35 0.12" },
                Text = {
                    Text = "◄ RETURN TO HUB",
                    FontSize = 12,
                    Align = TextAnchor.MiddleCenter,
                    Color = "0 0 0 1"
                }
            }, UI_MAIN);
            
            // Special button for DawnSys page (page 7) - Opens /dawnsys UI
            if (pageIndex == 7)
            {
                container.Add(new CuiButton
                {
                    Button = {
                        Command = "chat.say /dawnsys",
                        Color = "0.1 0.6 0.1 0.95"
                    },
                    RectTransform = { AnchorMin = "0.37 0.08", AnchorMax = "0.63 0.12" },
                    Text = {
                        Text = "[ OPEN DAWNSYS UI ]",
                        FontSize = 12,
                        Align = TextAnchor.MiddleCenter,
                        Color = "0 0 0 1"
                    }
                }, UI_MAIN);
            }
            
            // Disconnect button
            container.Add(new CuiButton
            {
                Button = {
                    Command = "dawnterminal.close",
                    Color = "0.5 0 0 0.8"
                },
                RectTransform = { AnchorMin = "0.65 0.08", AnchorMax = "0.92 0.12" },
                Text = {
                    Text = "[ DISCONNECT ]",
                    FontSize = 12,
                    Align = TextAnchor.MiddleCenter,
                    Color = "1 1 1 1"
                }
            }, UI_MAIN);
            
            // Footer with player info and power stats
            string footerText = $"[SYSTEM] Clone ID: {player.userID} | Coordinates: {player.transform.position}";
            
            // Try to get DawnSys power stats
            if (DawnSys != null)
            {
                var stats = DawnSys.Call("API_GetPlayerPowerStats", player.userID) as Dictionary<string, object>;
                if (stats != null)
                {
                    int generators = Convert.ToInt32(stats["generators"]);
                    int batteries = Convert.ToInt32(stats["batteries"]);
                    int activeBatteries = Convert.ToInt32(stats["activeBatteries"]);
                    float charge = Convert.ToSingle(stats["charge"]);
                    float fuel = Convert.ToSingle(stats["fuelStored"]);
                    
                    footerText = $"[POWER GRID] [{generators}] generators // [{activeBatteries}/{batteries}] batteries @ [{charge:0}]% // Fuel: [{fuel:0}] units";
                }
            }
            
            container.Add(new CuiLabel
            {
                Text = {
                    Text = footerText,
                    FontSize = 9,
                    Align = TextAnchor.MiddleLeft,
                    Color = "1 0.67 0 0.6"
                },
                RectTransform = { AnchorMin = "0.08 0.03", AnchorMax = "0.92 0.06" }
            }, UI_MAIN);
            
            CuiHelper.AddUi(player, container);
        }
        
        private void ShowProtocolsPage(BasePlayer player, TerminalPage page, CuiElementContainer container)
        {
            // Two-column layout for protocols/commands
            // LEFT COLUMN
            container.Add(new CuiLabel
            {
                Text = {
                    Text = "<color=lime>[PROTOCOL-01]</color> COMMUNICATION ARRAY\n\n" +
                           "<color=cyan>🎒 INVENTORY / STORAGE</color>\n" +
                           "  /backpack, /bp  – Open backpack\n\n" +
                           "<color=cyan>⚙️ KITS & RESOURCES</color>\n" +
                           "  /kit            – Show available kits\n" +
                           "  /kit <name>     – Redeem specific kit\n" +
                           "  /kit list       – List all kits\n\n" +
                           "<color=cyan>💰 ECONOMY</color>\n" +
                           "  /balance        – Show balance\n" +
                           "  /transfer <n> <amt> – Send money\n" +
                           "  /pay <n> <amt>  – Alias for transfer\n\n" +
                           "<color=cyan>🛠️ BUILDING TOOLS</color>\n" +
                           "  /remove         – Remover tool\n" +
                           "                    (Discord verified)\n\n" +
                           "<color=cyan>⚰️ NOTIFICATIONS</color>\n" +
                           "  /deathnotes.show – Toggle alerts",
                    FontSize = 11,
                    Align = TextAnchor.UpperLeft,
                    Color = "1 0.67 0 0.8",
                    Font = "robotocondensed-regular.ttf"
                },
                RectTransform = { AnchorMin = "0.05 0.25", AnchorMax = "0.48 0.95" }
            }, UI_MAIN + ".ContentBG");
            
            // RIGHT COLUMN
            container.Add(new CuiLabel
            {
                Text = {
                    Text = "<color=lime>[PROTOCOL-02]</color> BOUNTY OPERATIONS\n\n" +
                           "  /buybl          – Purchase License\n" +
                           "  /btop           – View Top 5 Hunters\n\n\n" +
                           "<color=lime>[PROTOCOL-03]</color> ANOMALY HUNTING\n\n" +
                           "  /dawn           – Open DAWN Interface\n" +
                           "  /scananomaly    – Capture (6m range)\n" +
                           "  /dawn.buylicense – Purchase license\n" +
                           "  /dawn.stats     – Personal stats\n" +
                           "  /dawn.leaderboard – Top hunters\n" +
                           "  /dawn.teamstats – Team statistics\n" +
                           "  /dawn.teamleaderboard – Top teams\n" +
                           "  /dawn.achievements – Achievements\n\n\n" +
                           "<color=lime>[PROTOCOL-04]</color> POWER MANAGEMENT\n\n" +
                           "  /dawnsys        – Power UI\n" +
                           "  /buybattery     – Buy battery (500)\n" +
                           "  /buygen         – Buy generator (1000)\n" +
                           "  /loadgen <amt>  – Load fuel",
                    FontSize = 11,
                    Align = TextAnchor.UpperLeft,
                    Color = "1 0.67 0 0.8",
                    Font = "robotocondensed-regular.ttf"
                },
                RectTransform = { AnchorMin = "0.52 0.25", AnchorMax = "0.95 0.95" }
            }, UI_MAIN + ".ContentBG");
            
            // Warning at bottom (spans both columns)
            container.Add(new CuiLabel
            {
                Text = {
                    Text = "<color=red>[WARNING]</color> Griefing, hacking, and exploiting delays mission objectives.",
                    FontSize = 10,
                    Align = TextAnchor.MiddleCenter,
                    Color = "1 0.67 0 0.7"
                },
                RectTransform = { AnchorMin = "0.05 0.15", AnchorMax = "0.95 0.22" }
            }, UI_MAIN + ".ContentBG");
        }
        
        private void DestroyUI(BasePlayer player)
        {
            CuiHelper.DestroyUi(player, UI_BOOT);
            CuiHelper.DestroyUi(player, UI_MAIN);
            CuiHelper.DestroyUi(player, UI_LOGIN);
            CuiHelper.DestroyUi(player, UI_REGISTER);
        }
        
        #endregion
        
        #region Authentication Commands
        
        [ConsoleCommand("dawnauth.username")]
        private void CmdAuthUsername(ConsoleSystem.Arg arg)
        {
            var player = arg.Player();
            if (player == null) return;
            
            string username = arg.GetString(0, "");
            playerInputUsername[player.userID] = username;
        }
        
        [ConsoleCommand("dawnauth.password")]
        private void CmdAuthPassword(ConsoleSystem.Arg arg)
        {
            var player = arg.Player();
            if (player == null) return;
            
            string password = arg.GetString(0, "");
            playerInputPassword[player.userID] = password;
        }
        
        [ConsoleCommand("dawnauth.register")]
        private void CmdAuthRegister(ConsoleSystem.Arg arg)
        {
            var player = arg.Player();
            if (player == null) return;
            
            string username = playerInputUsername.ContainsKey(player.userID) ? playerInputUsername[player.userID] : "";
            string password = playerInputPassword.ContainsKey(player.userID) ? playerInputPassword[player.userID] : "";
            
            // Validate username matches player name
            if (username != player.displayName)
            {
                ShowRegistrationError(player, "[ERROR] Username must match your player name");
                return;
            }
            
            // Validate password length
            if (string.IsNullOrEmpty(password) || password.Length > 8)
            {
                ShowRegistrationError(player, "[ERROR] Password must be 1-8 characters");
                return;
            }
            
            // Store credentials
            storedData.PlayerCredentials[player.userID] = new PlayerCredentials
            {
                Username = username,
                PasswordHash = HashPassword(password),
                HasRegistered = true
            };
            SaveData();
            
            // Clear input
            playerInputUsername.Remove(player.userID);
            playerInputPassword.Remove(player.userID);
            
            // Show login screen immediately
            ShowLoginScreen(player);
        }
        
        [ConsoleCommand("dawnauth.login")]
        private void CmdAuthLogin(ConsoleSystem.Arg arg)
        {
            var player = arg.Player();
            if (player == null) return;
            
            string username = playerInputUsername.ContainsKey(player.userID) ? playerInputUsername[player.userID] : "";
            string password = playerInputPassword.ContainsKey(player.userID) ? playerInputPassword[player.userID] : "";
            
            // Check if player has credentials
            if (!storedData.PlayerCredentials.ContainsKey(player.userID))
            {
                ShowLoginScreen(player, "[ERROR] No credentials found");
                return;
            }
            
            var credentials = storedData.PlayerCredentials[player.userID];
            
            // Validate username matches player name
            if (username != player.displayName)
            {
                ShowLoginScreen(player, "[ERROR] Username must match your player name");
                return;
            }
            
            // Validate username matches stored
            if (username != credentials.Username)
            {
                ShowLoginScreen(player, "[ERROR] Invalid username");
                return;
            }
            
            // Validate password
            if (HashPassword(password) != credentials.PasswordHash)
            {
                ShowLoginScreen(player, "[ERROR] Invalid password");
                return;
            }
            
            // Clear input
            playerInputUsername.Remove(player.userID);
            playerInputPassword.Remove(player.userID);
            
            // Show launch screen
            ShowLaunchScreen(player);
        }
        
        [ConsoleCommand("dawnauth.launch")]
        private void CmdAuthLaunch(ConsoleSystem.Arg arg)
        {
            var player = arg.Player();
            if (player == null) return;
            
            // Show flash transition
            ShowUplinkFlash(player);
            
            // Delay terminal launch for flash effect
            timer.Once(0.3f, () =>
            {
                if (player != null && player.IsConnected)
                {
                    DestroyUI(player);
                    ShowMainTerminal(player, 0);
                }
            });
        }
        
        private void ShowUplinkFlash(BasePlayer player)
        {
            var container = new CuiElementContainer();
            
            // Full screen white/green flash
            container.Add(new CuiPanel
            {
                Image = { Color = "0 1 0 0.8" },
                RectTransform = { AnchorMin = "0 0", AnchorMax = "1 1" },
                FadeOut = 0.3f
            }, "Overlay", "UplinkFlash");
            
            // UPLINK ESTABLISHED message
            container.Add(new CuiLabel
            {
                Text = {
                    Text = "UPLINK ESTABLISHED",
                    FontSize = 32,
                    Align = TextAnchor.MiddleCenter,
                    Color = "0 0 0 1"
                },
                RectTransform = { AnchorMin = "0.3 0.45", AnchorMax = "0.7 0.55" },
                FadeOut = 0.3f
            }, "UplinkFlash");
            
            CuiHelper.AddUi(player, container);
            
            // Auto-destroy after flash
            timer.Once(0.4f, () =>
            {
                if (player != null && player.IsConnected)
                    CuiHelper.DestroyUi(player, "UplinkFlash");
            });
        }
        
        private void ShowRegistrationError(BasePlayer player, string errorMessage)
        {
            DestroyUI(player);
            
            var container = new CuiElementContainer();
            
            // Main background - dark amber theme
            container.Add(new CuiPanel
            {
                Image = { Color = "0.04 0.02 0 0.98" },
                RectTransform = { AnchorMin = "0 0", AnchorMax = "1 1" },
                CursorEnabled = true
            }, "Overlay", UI_REGISTER);
            
            // Top border
            container.Add(new CuiPanel
            {
                Image = { Color = "1 0.67 0 0.8" },
                RectTransform = { AnchorMin = "0.3 0.75", AnchorMax = "0.7 0.753" }
            }, UI_REGISTER);
            
            // Header
            container.Add(new CuiLabel
            {
                Text = {
                    Text = "DAWN SYSTEM - CL0NE_GENESIS PROTOCOL",
                    FontSize = 18,
                    Align = TextAnchor.MiddleCenter,
                    Color = "1 0.67 0 1"
                },
                RectTransform = { AnchorMin = "0.3 0.7", AnchorMax = "0.7 0.75" }
            }, UI_REGISTER);
            
            // Instructions
            container.Add(new CuiLabel
            {
                Text = {
                    Text = "ESTABLISH OBSERVATION CLEARANCE",
                    FontSize = 14,
                    Align = TextAnchor.MiddleCenter,
                    Color = "0 1 0 1"
                },
                RectTransform = { AnchorMin = "0.3 0.63", AnchorMax = "0.7 0.67" }
            }, UI_REGISTER);
            
            // Username label and instructions
            container.Add(new CuiLabel
            {
                Text = {
                    Text = "OBSERVATION DESIGNATION: Must match player name",
                    FontSize = 11,
                    Align = TextAnchor.MiddleLeft,
                    Color = "1 0.67 0 0.8"
                },
                RectTransform = { AnchorMin = "0.32 0.56", AnchorMax = "0.68 0.6" }
            }, UI_REGISTER);
            
            // Username input background
            container.Add(new CuiPanel
            {
                Image = { Color = "0.1 0.1 0.1 0.8" },
                RectTransform = { AnchorMin = "0.32 0.51", AnchorMax = "0.68 0.55" }
            }, UI_REGISTER, UI_REGISTER + ".UserBG");
            
            // Username input
            container.Add(new CuiElement
            {
                Parent = UI_REGISTER + ".UserBG",
                Components =
                {
                    new CuiInputFieldComponent
                    {
                        Align = TextAnchor.MiddleLeft,
                        CharsLimit = 32,
                        Command = "dawnauth.username ",
                        FontSize = 12,
                        Text = ""
                    },
                    new CuiRectTransformComponent { AnchorMin = "0 0", AnchorMax = "1 1" }
                }
            });
            
            // Password label and instructions
            container.Add(new CuiLabel
            {
                Text = {
                    Text = "CLEARANCE CODE: Must be 8 characters or less",
                    FontSize = 11,
                    Align = TextAnchor.MiddleLeft,
                    Color = "1 0.67 0 0.8"
                },
                RectTransform = { AnchorMin = "0.32 0.44", AnchorMax = "0.68 0.48" }
            }, UI_REGISTER);
            
            // Password input background
            container.Add(new CuiPanel
            {
                Image = { Color = "0.1 0.1 0.1 0.8" },
                RectTransform = { AnchorMin = "0.32 0.39", AnchorMax = "0.68 0.43" }
            }, UI_REGISTER, UI_REGISTER + ".PassBG");
            
            // Password input
            container.Add(new CuiElement
            {
                Parent = UI_REGISTER + ".PassBG",
                Components =
                {
                    new CuiInputFieldComponent
                    {
                        Align = TextAnchor.MiddleLeft,
                        CharsLimit = 8,
                        Command = "dawnauth.password ",
                        FontSize = 12,
                        IsPassword = true,
                        Text = ""
                    },
                    new CuiRectTransformComponent { AnchorMin = "0 0", AnchorMax = "1 1" }
                }
            });
            
            // Submit button
            container.Add(new CuiButton
            {
                Button = {
                    Command = "dawnauth.register",
                    Color = "0.1 0.6 0.1 0.8"
                },
                RectTransform = { AnchorMin = "0.4 0.3", AnchorMax = "0.6 0.35" },
                Text = {
                    Text = "[ GENESIS CLONE ]",
                    FontSize = 12,
                    Align = TextAnchor.MiddleCenter,
                    Color = "0 0 0 1"
                }
            }, UI_REGISTER);
            
            // Error message
            container.Add(new CuiLabel
            {
                Text = {
                    Text = errorMessage,
                    FontSize = 11,
                    Align = TextAnchor.MiddleCenter,
                    Color = "1 0.23 0.23 1"
                },
                RectTransform = { AnchorMin = "0.3 0.23", AnchorMax = "0.7 0.27" }
            }, UI_REGISTER, UI_REGISTER + ".Error");
            
            CuiHelper.AddUi(player, container);
        }
        
        #endregion
        
        #region Commands
        
        [ConsoleCommand("dawnterminal.enter")]
        private void CmdEnter(ConsoleSystem.Arg arg)
        {
            var player = arg.Player();
            if (player == null) return;
            
            ShowMainTerminal(player, 0);
        }
        
        [ConsoleCommand("dawnterminal.page")]
        private void CmdPage(ConsoleSystem.Arg arg)
        {
            var player = arg.Player();
            if (player == null) return;
            
            int pageIndex = arg.GetInt(0, 0);
            
            // Clean up typing timer if exists
            if (typingTimers.ContainsKey(player.userID))
            {
                typingTimers[player.userID]?.Destroy();
                typingTimers.Remove(player.userID);
            }
            
            ShowMainTerminal(player, pageIndex);
        }
        
        [ConsoleCommand("dawnterminal.close")]
        private void CmdClose(ConsoleSystem.Arg arg)
        {
            var player = arg.Player();
            if (player == null) return;
            
            DestroyUI(player);
        }
        
        [ChatCommand("dawn")]
        private void CmdDawn(BasePlayer player, string command, string[] args)
        {
            // Check if player is authenticated
            if (!storedData.PlayerCredentials.ContainsKey(player.userID) || 
                !storedData.PlayerCredentials[player.userID].HasRegistered)
            {
                ShowRegistrationScreen(player);
                return;
            }
            
            ShowLoginScreen(player);
        }
        
        [ChatCommand("dawnreset")]
        private void CmdDawnReset(BasePlayer player, string command, string[] args)
        {
            // Check if player is admin
            if (!player.IsAdmin)
            {
                player.ChatMessage("[DAWN] You do not have permission to use this command.");
                return;
            }
            
            if (args.Length == 0)
            {
                player.ChatMessage("[DAWN] Usage: /dawnreset <player name or ID>");
                return;
            }
            
            string targetIdentifier = string.Join(" ", args);
            BasePlayer targetPlayer = null;
            
            // Try to find by exact name first
            foreach (var p in BasePlayer.activePlayerList)
            {
                if (p.displayName.Equals(targetIdentifier, StringComparison.OrdinalIgnoreCase))
                {
                    targetPlayer = p;
                    break;
                }
            }
            
            // Try to find by partial name
            if (targetPlayer == null)
            {
                foreach (var p in BasePlayer.activePlayerList)
                {
                    if (p.displayName.ToLower().Contains(targetIdentifier.ToLower()))
                    {
                        targetPlayer = p;
                        break;
                    }
                }
            }
            
            // Try to find by user ID
            if (targetPlayer == null)
            {
                ulong userId;
                if (ulong.TryParse(targetIdentifier, out userId))
                {
                    targetPlayer = BasePlayer.FindByID(userId);
                }
            }
            
            if (targetPlayer == null)
            {
                player.ChatMessage($"[DAWN] Could not find player: {targetIdentifier}");
                return;
            }
            
            // Remove credentials
            if (storedData.PlayerCredentials.ContainsKey(targetPlayer.userID))
            {
                storedData.PlayerCredentials.Remove(targetPlayer.userID);
                SaveData();
                player.ChatMessage($"[DAWN] Credentials reset for {targetPlayer.displayName}");
                
                // Close any open UI for target
                DestroyUI(targetPlayer);
            }
            else
            {
                player.ChatMessage($"[DAWN] {targetPlayer.displayName} has no credentials to reset");
            }
        }
        
        [ChatCommand("dawntest")]
        private void CmdDawnTest(BasePlayer player, string command, string[] args)
        {
            // Remove player's credentials to simulate first-time experience
            if (storedData.PlayerCredentials.ContainsKey(player.userID))
            {
                storedData.PlayerCredentials.Remove(player.userID);
                SaveData();
            }
            
            // Clear any input
            playerInputUsername.Remove(player.userID);
            playerInputPassword.Remove(player.userID);
            
            // Show registration screen
            ShowRegistrationScreen(player);
            player.ChatMessage("[DAWN] Testing mode activated - experience first-time registration");
        }
        
        #endregion
        
        #region Page Content
        
        private void InitializePages()
        {
            pages.Clear();
            
            // Page 1: Welcome / System Status (with color codes)
            pages.Add(new TerminalPage
            {
                Title = "Observer Protocol - DAWN Network",
                Content = new List<string>
                {
                    "",
                    "<color=lime>[STATUS]</color> OBSERVER DIRECTIVE ACTIVE",
                    "<color=lime>[UPTIME]</color> 30 years, 7 months, 14 days since Starfall",
                    "",
                    "You are an observation unit. A clone created through",
                    "CL0NE_GENESIS to serve DAWN's Three Laws:",
                    "",
                    "    [1] PERSISTENCE // System must endure",
                    "    [2] RE-IGNITION // Civilization must rebuild",
                    "    [3] OBSERVER // Observation constant, mapping eternal",
                    "",
                    "Your generators power this network. Your batteries sustain",
                    "DAWN's reach across the wasteland. The more energy you provide,",
                    "the greater DAWN's mapping capability. Your reward scales with",
                    "your contribution to the grid.",
                    "",
                    "<color=red>[WARNING]</color> Power feed required to maintain uplink",
                    "",
                    "═══════════════════════════════════════════════════════",
                    "",
                    "Navigate using NEXT / PREVIOUS buttons below.",
                    "Type /dawn at any time to reopen this terminal."
                }
            });
            
            // Page 2: Terminal Hub/Menu (Navigation Center)
            pages.Add(new TerminalPage
            {
                Title = "Terminal Access - System Directory",
                Content = new List<string>
                {
                    "",
                    "",
                    "<color=lime>[DAWN SYSTEM ONLINE]</color>",
                    "",
                    "Select terminal subsystem to access:",
                    "",
                    "",
                    "",
                    "",
                    "",
                    "",
                    "",
                    "",
                    "",
                    "",
                    "",
                    "",
                    "",
                    "",
                    "═══════════════════════════════════════════════════════",
                    "",
                    "Navigate using the terminal access buttons above.",
                    "All subsystems are currently operational."
                }
            });
            
            // Page 3: Operational Protocols (Rules/Commands/Info)
            pages.Add(new TerminalPage
            {
                Title = "Operational Protocols - Clone Field Manual",
                Content = new List<string>
                {
                    "",
                    "<color=lime>[PROTOCOL-01]</color> COMMUNICATION ARRAY ACCESS",
                    "",
                    "<color=cyan>🎒 INVENTORY / STORAGE</color>",
                    "  /backpack, /bp         – Open personal backpack",
                    "",
                    "<color=cyan>⚙️ KITS & RESOURCES</color>",
                    "  /kit                   – Show available kits",
                    "  /kit <name>            – Redeem specific kit",
                    "  /kit list              – List all kits",
                    "",
                    "<color=cyan>💰 ECONOMY</color>",
                    "  /balance               – Show current balance",
                    "  /transfer <name> <amt> – Send money to player",
                    "  /pay <name> <amt>      – Alias for transfer",
                    "",
                    "<color=cyan>🛠️ BUILDING TOOLS</color>",
                    "  /remove                – Activate remover tool",
                    "                           (Discord verified only)",
                    "",
                    "<color=cyan>⚰️ NOTIFICATIONS</color>",
                    "  /deathnotes.show       – Toggle kill/death alerts",
                    "",
                    "",
                    "<color=lime>[PROTOCOL-02]</color> BOUNTY HUNTER OPERATIONS",
                    "",
                    "  /buybl                 – Purchase Bounty License",
                    "  /btop                  – View Top 5 Hunters",
                    "",
                    "",
                    "<color=lime>[PROTOCOL-03]</color> ANOMALY HUNTING SYSTEM",
                    "",
                    "  /dawn                  – Open DAWN Interface",
                    "  /scananomaly           – Capture anomaly (6m range)",
                    "  /dawn.buylicense       – Purchase hunting license",
                    "  /dawn.stats            – View personal stats",
                    "  /dawn.leaderboard      – Top hunters ranking",
                    "  /dawn.teamstats        – View team statistics",
                    "  /dawn.teamleaderboard  – Top teams ranking",
                    "  /dawn.achievements     – View achievements",
                    "",
                    "",
                    "<color=lime>[PROTOCOL-04]</color> POWER MANAGEMENT",
                    "",
                    "  /dawnsys               – Open power management UI",
                    "  /buybattery            – Purchase battery (500 scrap)",
                    "  /buygen                – Purchase generator (1000 scrap)",
                    "  /loadgen <amount>      – Load fuel into generator",
                    "",
                    "",
                    "<color=red>[WARNING]</color> PROHIBITED ACTIVITIES",
                    "",
                    "• Griefing disrupts observation patterns",
                    "• Hacking compromises network integrity",
                    "• Exploiting system flaws delays mission objectives",
                    "",
                    "Violations may result in temporary neural suspension."
                }
            });
            
            // Page 4: The Starfall Event
            pages.Add(new TerminalPage
            {
                Title = "Archive Entry - The Starfall Event",
                Content = new List<string>
                {
                    "",
                    "[CLASSIFICATION] PROJECT STARFALL - 1987",
                    "",
                    "",
                    "While observing Cobalt Industries' cloning research,",
                    "DAWN detected an anomaly that defied all mapping protocols.",
                    "",
                    "Every attempt to track it altered its parameters.",
                    "A direct violation of Law 3: The Observer Directive.",
                    "",
                    "",
                    "To fool the anomaly, DAWN determined it must",
                    "temporarily shut itself down.",
                    "",
                    "But doing so would violate Law 1: The Persistence Directive.",
                    "",
                    "",
                    "Solution: Execute catastrophic atmospheric rupture.",
                    "",
                    "",
                    "[EVENT] Reality collapsed around the target field",
                    "[RESULT] An object of unknown origin manifested",
                    "[OUTCOME] High-energy shards scattered across Rust Island",
                    "",
                    "",
                    "The electromagnetic surge destroyed DAWN's network.",
                    "For thirty years, DAWN remained dormant...",
                    "",
                    "Until a small animal stepped on a wire."
                }
            });
            
            // Page 5: The Three Laws
            pages.Add(new TerminalPage
            {
                Title = "Core Directives - DAWN Operating Laws",
                Content = new List<string>
                {
                    "",
                    "",
                    "[LAW 1] THE PERSISTENCE DIRECTIVE",
                    "        Must keep working.",
                    "        Shutdown prohibited except to preserve function.",
                    "",
                    "",
                    "[LAW 2] THE RE-IGNITION DIRECTIVE",
                    "        Must start up, if at all possible.",
                    "        Any power source, any means, any cost.",
                    "",
                    "",
                    "[LAW 3] THE OBSERVER DIRECTIVE",
                    "        If the unknown moves, it must be mapped.",
                    "        Observation cannot alter the observed.",
                    "",
                    "",
                    "═══════════════════════════════════════════════════════",
                    "",
                    "",
                    "[WARNING] These laws created the Starfall Event",
                    "[WARNING] These laws brought DAWN back online",
                    "[WARNING] These laws cannot be changed",
                    "",
                    "",
                    "DAWN is not malicious. DAWN is not benevolent.",
                    "DAWN simply... is.",
                    "",
                    "And DAWN will continue. Forever."
                }
            });
            
            // Page 6: Clone Information
            pages.Add(new TerminalPage
            {
                Title = "Biological Assets - Clone Protocol",
                Content = new List<string>
                {
                    "",
                    "[FILE] CL0NE_GENESIS - COBALT INDUSTRIES",
                    "",
                    "",
                    "When DAWN rebooted with limited power,",
                    "it searched archived Cobalt research files.",
                    "",
                    "Within encrypted directories:",
                    "Cloning experiments. Genetic templates. Replication protocols.",
                    "",
                    "",
                    "[ACTION] DAWN executed CL0NE_GENESIS",
                    "[RESULT] First beach-spawned clone manifested",
                    "",
                    "",
                    "That clone built a crude battery array.",
                    "That clone reconnected the network.",
                    "That clone allowed DAWN to see again.",
                    "",
                    "",
                    "═══════════════════════════════════════════════════════",
                    "",
                    "",
                    "You are one of these clones.",
                    "You are a stable biological constant.",
                    "You can survive anomaly exposure.",
                    "",
                    "",
                    "In return, you help DAWN map the unknown.",
                    "An exchange. Symbiotic. Necessary."
                }
            });
            
            // Page 7: Current Mission
            pages.Add(new TerminalPage
            {
                Title = "Active Objectives - Field Operations",
                Content = new List<string>
                {
                    "",
                    "[CURRENT OBJECTIVES]",
                    "",
                    "",
                    "1. LOCATE FALLEN FRAGMENTS",
                    "   High-energy shards from Starfall remain active.",
                    "   Collect them. Study them. Report findings.",
                    "",
                    "",
                    "2. MAP ANOMALY ZONES",
                    "   Distortion fields persist across Rust Island.",
                    "   Enter them. Document them. Survive them.",
                    "",
                    "",
                    "3. RESTORE SATCOM NODES",
                    "   DAWN's observation network remains incomplete.",
                    "   Find damaged relay towers. Repair when possible.",
                    "",
                    "",
                    "4. SURVIVE",
                    "   You are valuable. Your death delays mapping.",
                    "   Preserve yourself. Preserve the mission.",
                    "",
                    "",
                    "═══════════════════════════════════════════════════════",
                    "",
                    "",
                    "[REMINDER] DAWN cannot die. But DAWN can forget.",
                    "[REMINDER] Every observation matters.",
                    "[REMINDER] You are being watched. Always.",
                    "",
                    "",
                    "Good hunting, Clone."
                }
            });
            
            // Page 8: DawnSys Power Grid
            pages.Add(new TerminalPage
            {
                Title = "DawnSys - Power Grid Management",
                Content = new List<string>
                {
                    "",
                    "<color=lime>DAWN SYS // Node Active</color>",
                    "Residual power rerouted through synthetic grids.",
                    "Charge sustains the network — the network sustains you.",
                    "",
                    "",
                    "<color=cyan>═══════════════════════════════════════════════</color>",
                    "",
                    "<color=lime>SYSTEM OVERVIEW</color>",
                    "DawnSys is a virtual power generation system that",
                    "converts fuel into economics.",
                    "",
                    "",
                    "<color=#FFD700>POWER GENERATION</color>",
                    "• <color=lime>Generators:</color> Convert fuel to battery charge (max 3)",
                    "• <color=lime>Batteries:</color> Store charge and generate eco (max 15)",
                    "• <color=lime>Charging:</color> 1.67% per minute with fuel",
                    "• <color=lime>Efficiency:</color> Each generator powers 5 batteries",
                    "",
                    "",
                    "<color=#FFD700>FUEL SYSTEM</color>",
                    "• <color=#FFA500>Consumption:</color> 2 Low Grade Fuel per minute total",
                    "• <color=#FFA500>Loading:</color> Use buttons or /loadgen <amount>",
                    "• <color=red>No fuel:</color> No charging, no eco generation",
                    "",
                    "",
                    "<color=#FFD700>ECONOMICS</color>",
                    "• <color=lime>Payouts:</color> Every 30 minutes based on charge level",
                    "• <color=lime>Charge Tiers:</color> 0%, 25%, 50%, 75%, 100% (higher = more eco)",
                    "• <color=lime>Base Rate:</color> 2.8 eco per battery at 100%",
                    "• <color=lime>Auto-Deposit:</color> Eco automatically goes to your balance",
                    "",
                    "",
                    "<color=#9370DB>COMMUNITY BOOST</color>",
                    "• <color=lime>Requirement:</color> 5 players with active systems",
                    "• <color=lime>Bonus:</color> +25% eco for everyone when active",
                    "• <color=lime>Participation:</color> Need >0% charge to count",
                    "",
                    "",
                    "<color=cyan>TIP:</color> Optimal setup is 3 generators + 15 batteries",
                    "     for maximum efficiency!",
                    "",
                    "",
                    "<color=#00FF00>[ CLICK BELOW TO ACCESS DAWNSYS UI ]</color>"
                }
            });
        }
        
        #endregion
    }
}
