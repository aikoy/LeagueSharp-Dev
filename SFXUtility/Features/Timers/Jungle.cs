﻿#region License

/*
 Copyright 2014 - 2015 Nikita Bernthaler
 Jungle.cs is part of SFXUtility.

 SFXUtility is free software: you can redistribute it and/or modify
 it under the terms of the GNU General Public License as published by
 the Free Software Foundation, either version 3 of the License, or
 (at your option) any later version.

 SFXUtility is distributed in the hope that it will be useful,
 but WITHOUT ANY WARRANTY; without even the implied warranty of
 MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE. See the
 GNU General Public License for more details.

 You should have received a copy of the GNU General Public License
 along with SFXUtility. If not, see <http://www.gnu.org/licenses/>.
*/

#endregion License

namespace SFXUtility.Features.Timers
{
    #region

    using System;
    using System.Collections.Generic;
    using System.Linq;
    using Classes;
    using LeagueSharp;
    using LeagueSharp.Common;
    using SFXLibrary;
    using SFXLibrary.Extensions.NET;
    using SFXLibrary.Logger;
    using SharpDX;

    #endregion

    internal class Jungle : Base
    {
        private readonly List<Camp> _camps = new List<Camp>();
        private Timers _parent;

        public override bool Enabled
        {
            get { return _parent != null && _parent.Enabled && Menu != null && Menu.Item(Name + "Enabled").GetValue<bool>(); }
        }

        public override string Name
        {
            get { return Language.Get("F_Jungle"); }
        }

        protected override void OnEnable()
        {
            foreach (var camp in _camps)
            {
                camp.Active = true;
            }
            GameObject.OnCreate += OnGameObjectCreate;
            GameObject.OnDelete += OnGameObjectDelete;
            base.OnEnable();
        }

        protected override void OnDisable()
        {
            foreach (var camp in _camps)
            {
                camp.Active = false;
            }
            GameObject.OnCreate -= OnGameObjectCreate;
            GameObject.OnDelete -= OnGameObjectDelete;
            base.OnDisable();
        }

        private void OnGameObjectDelete(GameObject sender, EventArgs args)
        {
            try
            {
                if (!sender.IsValid || sender.Type != GameObjectType.obj_AI_Minion || sender.Team != GameObjectTeam.Neutral)
                    return;

                foreach (var camp in _camps)
                {
                    var mob = camp.Mobs.FirstOrDefault(m => m.Name.Contains(sender.Name, StringComparison.OrdinalIgnoreCase));
                    if (mob != null)
                    {
                        mob.Dead = true;
                        camp.Dead = camp.Mobs.All(m => m.Dead);
                        if (camp.Dead)
                        {
                            camp.Dead = true;
                            camp.NextRespawnTime = (int) Game.ClockTime + camp.RespawnTime - 5;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Global.Logger.AddItem(new LogItem(ex));
            }
        }

        private void OnGameObjectCreate(GameObject sender, EventArgs args)
        {
            if (!sender.IsValid || sender.Type != GameObjectType.obj_AI_Minion || sender.Team != GameObjectTeam.Neutral)
                return;

            foreach (var camp in _camps)
            {
                var mob = camp.Mobs.FirstOrDefault(m => m.Name.Contains(sender.Name, StringComparison.OrdinalIgnoreCase));
                if (mob != null)
                {
                    mob.Dead = false;
                    camp.Dead = false;
                }
            }
        }

        protected override void OnGameLoad(EventArgs args)
        {
            try
            {
                if (Global.IoC.IsRegistered<Timers>())
                {
                    _parent = Global.IoC.Resolve<Timers>();
                    if (_parent.Initialized)
                        OnParentInitialized(null, null);
                    else
                        _parent.OnInitialized += OnParentInitialized;
                }
            }
            catch (Exception ex)
            {
                Global.Logger.AddItem(new LogItem(ex));
            }
        }

        private void OnParentInitialized(object sender, EventArgs eventArgs)
        {
            try
            {
                if (_parent.Menu == null)
                    return;

                Menu = new Menu(Name, Name);

                var drawingMenu = new Menu(Language.Get("G_Drawing"), Name + "Drawing");
                var drawingMapMenu = new Menu(Language.Get("G_Map"), drawingMenu.Name + "Map");
                var drawingMinimapMenu = new Menu(Language.Get("G_Minimap"), drawingMenu.Name + "Minimap");

                drawingMapMenu.AddItem(
                    new MenuItem(drawingMapMenu.Name + "TimeFormat", Language.Get("G_TimeFormat")).SetValue(new StringList(new[] {"mm:ss", "ss"})));
                drawingMapMenu.AddItem(new MenuItem(drawingMapMenu.Name + "FontSize", Language.Get("G_FontSize")).SetValue(new Slider(20, 3, 30)));
                drawingMapMenu.AddItem(new MenuItem(drawingMapMenu.Name + "Enabled", Language.Get("G_Enabled")).SetValue(false));

                drawingMinimapMenu.AddItem(
                    new MenuItem(drawingMinimapMenu.Name + "TimeFormat", Language.Get("G_TimeFormat")).SetValue(new StringList(new[] {"mm:ss", "ss"})));
                drawingMinimapMenu.AddItem(
                    new MenuItem(drawingMinimapMenu.Name + "FontSize", Language.Get("G_FontSize")).SetValue(new Slider(13, 3, 30)));
                drawingMinimapMenu.AddItem(new MenuItem(drawingMinimapMenu.Name + "Enabled", Language.Get("G_Enabled")).SetValue(false));

                drawingMenu.AddSubMenu(drawingMapMenu);
                drawingMenu.AddSubMenu(drawingMinimapMenu);

                Menu.AddSubMenu(drawingMenu);

                Menu.AddItem(new MenuItem(Name + "Enabled", Language.Get("G_Enabled")).SetValue(false));

                Menu.Item(Name + "DrawingMapTimeFormat").ValueChanged +=
                    delegate(object o, OnValueChangeEventArgs args)
                    {
                        _camps.ForEach(c => c.MapTotalSeconds = args.GetNewValue<StringList>().SelectedIndex == 1);
                    };

                Menu.Item(Name + "DrawingMinimapTimeFormat").ValueChanged +=
                    delegate(object o, OnValueChangeEventArgs args)
                    {
                        _camps.ForEach(c => c.MinimapTotalSeconds = args.GetNewValue<StringList>().SelectedIndex == 1);
                    };

                Menu.Item(Name + "DrawingMapEnabled").ValueChanged +=
                    delegate(object o, OnValueChangeEventArgs args) { _camps.ForEach(c => c.MapEnabled = args.GetNewValue<bool>()); };

                Menu.Item(Name + "DrawingMinimapEnabled").ValueChanged +=
                    delegate(object o, OnValueChangeEventArgs args) { _camps.ForEach(c => c.MinimapEnabled = args.GetNewValue<bool>()); };

                _parent.Menu.AddSubMenu(Menu);

                SetupCamps();

                if (_camps.Count == 0)
                    return;

                HandleEvents(_parent);
                RaiseOnInitialized();
            }
            catch (Exception ex)
            {
                Global.Logger.AddItem(new LogItem(ex));
            }
        }

        private void SetupCamps()
        {
            var mapFontSize = Menu.Item(Name + "DrawingMapFontSize").GetValue<Slider>().Value;
            var minimapFontSize = Menu.Item(Name + "DrawingMinimapFontSize").GetValue<Slider>().Value;
            var mapTotalSeconds = Menu.Item(Name + "DrawingMapTimeFormat").GetValue<StringList>().SelectedIndex == 1;
            var minimapTotalSeconds = Menu.Item(Name + "DrawingMinimapTimeFormat").GetValue<StringList>().SelectedIndex == 1;
            var mapEnabled = Menu.Item(Name + "DrawingMapEnabled").GetValue<bool>();
            var minimapEnabled = Menu.Item(Name + "DrawingMinimapEnabled").GetValue<bool>();

            switch (Utility.Map.GetMap().Type)
            {
                case Utility.Map.MapType.SummonersRift:
                    _camps.AddRange(new List<Camp>
                    {
// Order: Blue
                        new Camp(115, 300, new Vector3(3800.99f, 7883.53f, 52.18f),
                            new[] {new Mob("SRU_Blue1.1.1"), new Mob("SRU_BlueMini1.1.2"), new Mob("SRU_BlueMini21.1.3")}, mapFontSize,
                            minimapFontSize)
                        {
                            MapTotalSeconds = mapTotalSeconds,
                            MinimapTotalSeconds = minimapTotalSeconds,
                            MapEnabled = mapEnabled,
                            MinimapEnabled = minimapEnabled
                        },
                        //Order: Wolves
                        new Camp(115, 100, new Vector3(3849.95f, 6504.36f, 52.46f),
                            new[] {new Mob("SRU_Murkwolf2.1.1"), new Mob("SRU_MurkwolfMini2.1.2"), new Mob("SRU_MurkwolfMini2.1.3")}, mapFontSize,
                            minimapFontSize)
                        {
                            MapTotalSeconds = mapTotalSeconds,
                            MinimapTotalSeconds = minimapTotalSeconds,
                            MapEnabled = mapEnabled,
                            MinimapEnabled = minimapEnabled
                        },
                        //Order: Chicken
                        new Camp(115, 100, new Vector3(6943.41f, 5422.61f, 52.62f),
                            new[]
                            {
                                new Mob("SRU_Razorbeak3.1.1"), new Mob("SRU_RazorbeakMini3.1.2"), new Mob("SRU_RazorbeakMini3.1.3"),
                                new Mob("SRU_RazorbeakMini3.1.4")
                            }, mapFontSize, minimapFontSize)
                        {
                            MapTotalSeconds = mapTotalSeconds,
                            MinimapTotalSeconds = minimapTotalSeconds,
                            MapEnabled = mapEnabled,
                            MinimapEnabled = minimapEnabled
                        },
                        //Order: Red
                        new Camp(115, 300, new Vector3(7813.07f, 4051.33f, 53.81f),
                            new[] {new Mob("SRU_Red4.1.1"), new Mob("SRU_RedMini4.1.2"), new Mob("SRU_RedMini4.1.3")}, mapFontSize, minimapFontSize)
                        {
                            MapTotalSeconds = mapTotalSeconds,
                            MinimapTotalSeconds = minimapTotalSeconds,
                            MapEnabled = mapEnabled,
                            MinimapEnabled = minimapEnabled
                        },
                        //Order: Krug
                        new Camp(115, 100, new Vector3(8370.58f, 2718.15f, 51.09f), new[] {new Mob("SRU_Krug5.1.2"), new Mob("SRU_KrugMini5.1.1")},
                            mapFontSize, minimapFontSize)
                        {
                            MapTotalSeconds = mapTotalSeconds,
                            MinimapTotalSeconds = minimapTotalSeconds,
                            MapEnabled = mapEnabled,
                            MinimapEnabled = minimapEnabled
                        },
                        //Order: Gromp
                        new Camp(115, 100, new Vector3(2164.34f, 8383.02f, 51.78f), new[] {new Mob("SRU_Gromp13.1.1")}, mapFontSize, minimapFontSize)
                        {
                            MapTotalSeconds = mapTotalSeconds,
                            MinimapTotalSeconds = minimapTotalSeconds,
                            MapEnabled = mapEnabled,
                            MinimapEnabled = minimapEnabled
                        },

                        //Chaos: Blue
                        new Camp(115, 300, new Vector3(10984.11f, 6960.31f, 51.72f),
                            new[] {new Mob("SRU_Blue7.1.1"), new Mob("SRU_BlueMini7.1.2"), new Mob("SRU_BlueMini27.1.3")}, mapFontSize,
                            minimapFontSize)
                        {
                            MapTotalSeconds = mapTotalSeconds,
                            MinimapTotalSeconds = minimapTotalSeconds,
                            MapEnabled = mapEnabled,
                            MinimapEnabled = minimapEnabled
                        },
                        //Chaos: Wolves
                        new Camp(115, 100, new Vector3(10983.83f, 8328.73f, 62.22f),
                            new[] {new Mob("SRU_Murkwolf8.1.1"), new Mob("SRU_MurkwolfMini8.1.2"), new Mob("SRU_MurkwolfMini8.1.3")}, mapFontSize,
                            minimapFontSize)
                        {
                            MapTotalSeconds = mapTotalSeconds,
                            MinimapTotalSeconds = minimapTotalSeconds,
                            MapEnabled = mapEnabled,
                            MinimapEnabled = minimapEnabled
                        },
                        //Chaos: Chicken
                        new Camp(115, 100, new Vector3(7852.38f, 9562.62f, 52.30f),
                            new[]
                            {
                                new Mob("SRU_Razorbeak9.1.1"), new Mob("SRU_RazorbeakMini9.1.2"), new Mob("SRU_RazorbeakMini9.1.3"),
                                new Mob("SRU_RazorbeakMini9.1.4")
                            }, mapFontSize, minimapFontSize)
                        {
                            MapTotalSeconds = mapTotalSeconds,
                            MinimapTotalSeconds = minimapTotalSeconds,
                            MapEnabled = mapEnabled,
                            MinimapEnabled = minimapEnabled
                        },
                        //Chaos: Red
                        new Camp(115, 300, new Vector3(7139.29f, 10779.34f, 56.38f),
                            new[] {new Mob("SRU_Red10.1.1"), new Mob("SRU_RedMini10.1.2"), new Mob("SRU_RedMini10.1.3")}, mapFontSize, minimapFontSize)
                        {
                            MapTotalSeconds = mapTotalSeconds,
                            MinimapTotalSeconds = minimapTotalSeconds,
                            MapEnabled = mapEnabled,
                            MinimapEnabled = minimapEnabled
                        },
                        //Chaos: Krug
                        new Camp(115, 100, new Vector3(6476.17f, 12142.51f, 56.48f), new[] {new Mob("SRU_Krug11.1.2"), new Mob("SRU_KrugMini11.1.1")},
                            mapFontSize, minimapFontSize)
                        {
                            MapTotalSeconds = mapTotalSeconds,
                            MinimapTotalSeconds = minimapTotalSeconds,
                            MapEnabled = mapEnabled,
                            MinimapEnabled = minimapEnabled
                        },
                        //Chaos: Gromp
                        new Camp(115, 100, new Vector3(12671.83f, 6306.60f, 51.71f), new[] {new Mob("SRU_Gromp14.1.1")}, mapFontSize, minimapFontSize)
                        {
                            MapTotalSeconds = mapTotalSeconds,
                            MinimapTotalSeconds = minimapTotalSeconds,
                            MapEnabled = mapEnabled,
                            MinimapEnabled = minimapEnabled
                        },

                        //Neutral: Dragon
                        new Camp(150, 360, new Vector3(9813.83f, 4360.19f, -71.24f), new[] {new Mob("SRU_Dragon6.1.1")}, mapFontSize, minimapFontSize)
                        {
                            MapTotalSeconds = mapTotalSeconds,
                            MinimapTotalSeconds = minimapTotalSeconds,
                            MapEnabled = mapEnabled,
                            MinimapEnabled = minimapEnabled
                        },
                        //Neutral: Baron
                        new Camp(120, 420, new Vector3(4993.14f, 10491.92f, -71.24f), new[] {new Mob("SRU_Baron12.1.1")}, mapFontSize, minimapFontSize)
                        {
                            MapTotalSeconds = mapTotalSeconds,
                            MinimapTotalSeconds = minimapTotalSeconds,
                            MapEnabled = mapEnabled,
                            MinimapEnabled = minimapEnabled
                        },
                        //Dragon: Crab
                        new Camp(150, 180, new Vector3(10647.70f, 5144.68f, -62.81f), new[] {new Mob("SRU_Crab15.1.1")}, mapFontSize, minimapFontSize)
                        {
                            MapTotalSeconds = mapTotalSeconds,
                            MinimapTotalSeconds = minimapTotalSeconds,
                            MapEnabled = mapEnabled,
                            MinimapEnabled = minimapEnabled
                        },
                        //Baron: Crab
                        new Camp(150, 180, new Vector3(4285.04f, 9597.52f, -67.60f), new[] {new Mob("SRU_Crab16.1.1")}, mapFontSize, minimapFontSize)
                        {
                            MapTotalSeconds = mapTotalSeconds,
                            MinimapTotalSeconds = minimapTotalSeconds,
                            MapEnabled = mapEnabled,
                            MinimapEnabled = minimapEnabled
                        }
                    });
                    break;
                case Utility.Map.MapType.TwistedTreeline:
                    _camps.AddRange(new List<Camp>
                    {
//Order: Wraiths
                        new Camp(100, 75, new Vector3(3550f, 6250f, 60f),
                            new[] {new Mob("TT_NWraith1.1.1"), new Mob("TT_NWraith21.1.2"), new Mob("TT_NWraith21.1.3")}, mapFontSize, minimapFontSize)
                        {
                            MapTotalSeconds = mapTotalSeconds,
                            MinimapTotalSeconds = minimapTotalSeconds,
                            MapEnabled = mapEnabled,
                            MinimapEnabled = minimapEnabled
                        },
                        //Order: Golems
                        new Camp(100, 75, new Vector3(4500f, 8550f, 60f), new[] {new Mob("TT_NGolem2.1.1"), new Mob("TT_NGolem22.1.2")}, mapFontSize,
                            minimapFontSize)
                        {
                            MapTotalSeconds = mapTotalSeconds,
                            MinimapTotalSeconds = minimapTotalSeconds,
                            MapEnabled = mapEnabled,
                            MinimapEnabled = minimapEnabled
                        },
                        //Order: Wolves
                        new Camp(100, 75, new Vector3(5600f, 6400f, 60f),
                            new[] {new Mob("TT_NWolf3.1.1"), new Mob("TT_NWolf23.1.2"), new Mob("TT_NWolf23.1.3")}, mapFontSize, minimapFontSize)
                        {
                            MapTotalSeconds = mapTotalSeconds,
                            MinimapTotalSeconds = minimapTotalSeconds,
                            MapEnabled = mapEnabled,
                            MinimapEnabled = minimapEnabled
                        },

                        //Chaos: Wraiths
                        new Camp(100, 75, new Vector3(10300f, 6250f, 60f),
                            new[] {new Mob("TT_NWraith4.1.1"), new Mob("TT_NWraith24.1.2"), new Mob("TT_NWraith24.1.3")}, mapFontSize, minimapFontSize)
                        {
                            MapTotalSeconds = mapTotalSeconds,
                            MinimapTotalSeconds = minimapTotalSeconds,
                            MapEnabled = mapEnabled,
                            MinimapEnabled = minimapEnabled
                        },
                        //Chaos: Golems
                        new Camp(100, 75, new Vector3(9800f, 8550f, 60f), new[] {new Mob("TT_NGolem5.1.1"), new Mob("TT_NGolem25.1.2")}, mapFontSize,
                            minimapFontSize)
                        {
                            MapTotalSeconds = mapTotalSeconds,
                            MinimapTotalSeconds = minimapTotalSeconds,
                            MapEnabled = mapEnabled,
                            MinimapEnabled = minimapEnabled
                        },
                        //Chaos: Wolves
                        new Camp(100, 75, new Vector3(8600f, 6400f, 60f),
                            new[] {new Mob("TT_NWolf6.1.1"), new Mob("TT_NWolf26.1.2"), new Mob("TT_NWolf26.1.3")}, mapFontSize, minimapFontSize)
                        {
                            MapTotalSeconds = mapTotalSeconds,
                            MinimapTotalSeconds = minimapTotalSeconds,
                            MapEnabled = mapEnabled,
                            MinimapEnabled = minimapEnabled
                        },

                        //Neutral: Vilemaw
                        new Camp(600, 300, new Vector3(7150f, 11100f, 60f), new[] {new Mob("TT_Spiderboss8.1.1")}, mapFontSize, minimapFontSize)
                        {
                            MapTotalSeconds = mapTotalSeconds,
                            MinimapTotalSeconds = minimapTotalSeconds,
                            MapEnabled = mapEnabled,
                            MinimapEnabled = minimapEnabled
                        }
                    });
                    break;
            }
        }

        private class Camp
        {
            private readonly Render.Text _mapText;
            private readonly Render.Text _minimapText;
            private bool _active;
            private bool _added;
            // ReSharper disable once UnusedParameter.Local
            public Camp(float spawnTime, float respawnTime, Vector3 position, Mob[] mobs, int mapFontSize, int minimapFontSize)
            {
                RespawnTime = respawnTime;
                Mobs = mobs;

                _mapText = new Render.Text(Vector2.Zero, string.Empty, mapFontSize, Color.White)
                {
                    OutLined = true,
                    Centered = true,
                    VisibleCondition = delegate
                    {
                        try
                        {
                            return Active && Dead && MapEnabled && position.IsOnScreen();
                        }
                        catch (Exception ex)
                        {
                            Global.Logger.AddItem(new LogItem(ex));
                            return false;
                        }
                    },
                    TextUpdate = delegate
                    {
                        try
                        {
                            if (NextRespawnTime - (int) Game.Time <= 0)
                                Dead = false;
                            return (NextRespawnTime - (int) Game.Time).FormatTime(MapTotalSeconds);
                        }
                        catch (Exception ex)
                        {
                            Global.Logger.AddItem(new LogItem(ex));
                            return string.Empty;
                        }
                    },
                    PositionUpdate = () => Drawing.WorldToScreen(position)
                };

                _minimapText = new Render.Text(Drawing.WorldToMinimap(position), string.Empty, minimapFontSize, Color.White)
                {
                    OutLined = true,
                    Centered = true,
                    VisibleCondition = delegate
                    {
                        try
                        {
                            return Active && Dead && MinimapEnabled;
                        }
                        catch (Exception ex)
                        {
                            Global.Logger.AddItem(new LogItem(ex));
                            return false;
                        }
                    },
                    TextUpdate = delegate
                    {
                        try
                        {
                            if (NextRespawnTime - (int) Game.Time <= 0)
                                Dead = false;
                            return (NextRespawnTime - (int) Game.Time).FormatTime(MinimapTotalSeconds);
                        }
                        catch (Exception ex)
                        {
                            Global.Logger.AddItem(new LogItem(ex));
                            return string.Empty;
                        }
                    }
                };
            }

            public bool MapTotalSeconds { private get; set; }
            public bool MinimapTotalSeconds { private get; set; }

            public bool MapEnabled { private get; set; }

            public bool MinimapEnabled { private get; set; }

            public bool Active
            {
                private get { return _active; }
                set
                {
                    _active = value;
                    Update();
                }
            }

            private void Update()
            {
                if (_active && !_added)
                {
                    _mapText.Add(0);
                    _minimapText.Add(0);
                    _added = true;
                }
                else if (!_active && _added)
                {
                    _mapText.Remove();
                    _minimapText.Remove();
                    _added = false;
                }
            }

            public float RespawnTime { get; private set; }
            public Mob[] Mobs { get; private set; }
            public float NextRespawnTime { private get; set; }
            public bool Dead { get; set; }
        }

        private class Mob
        {
            public Mob(string name, bool dead = true)
            {
                Name = name;
                Dead = dead;
            }

            public bool Dead { get; set; }
            public string Name { get; private set; }
        }
    }
}