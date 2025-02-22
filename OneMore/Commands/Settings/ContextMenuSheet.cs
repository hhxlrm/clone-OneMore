﻿//************************************************************************************************
// Copyright © 2020 Steven M. Cohn. All Rights Reserved.
//************************************************************************************************

#pragma warning disable S3267 // Loops should be simplified with "LINQ" expressions

namespace River.OneMoreAddIn.Settings
{
	using River.OneMoreAddIn.Helpers.Office;
	using System;
	using System.Collections.Generic;
	using System.Drawing;
	using System.Linq;
	using System.Reflection;
	using System.Windows.Forms;
	using System.Xml.Linq;
	using Resx = Properties.Resources;


	internal partial class ContextMenuSheet : SheetBase
	{
		private const string ProofingResID = "ribProofingMenu_Label";
		private const string ColorizeResID = "ribColorizeMenu_Label";

		#region Private classes
		private sealed class CtxMenu
		{
			public string Name { get; set; }
			public string ResID { get; set; }
			public IEnumerable<CtxMenuItem> Commands { get; set; }
			public override string ToString() => Name;
		}


		private sealed class CtxMenuItem
		{
			public string Name { get; set; }
			public string ResID { get; set; }
			public override string ToString() => Name;
		}


		private sealed class MenuPanel : FlowLayoutPanel
		{
			public MenuPanel()
			{
				FlowDirection = FlowDirection.LeftToRight;
				AutoScroll = true;
				WrapContents = true;
			}


			public void AddMenus(IEnumerable<CtxMenu> menus)
			{
				var width = 0;

				foreach (var menu in menus)
				{
					var mitem = new MenuItemPanel(menu);
					Controls.Add(mitem);

					// menu items are not indented but want to have the same right-alignment
					// as command items so add indent width into this calculation
					var offset = mitem.Width + SystemInformation.MenuCheckSize.Width;
					if (width < offset)
					{
						width = offset;
					}

					if (menu.ResID != ColorizeResID)
					{
						foreach (var command in menu.Commands)
						{
							var citem = new MenuItemPanel(command);
							Controls.Add(citem);
							if (width < citem.Width)
							{
								width = citem.Width;
							}
						}
					}
				}

				// a little extra
				width += 20;

				foreach (MenuItemPanel item in Controls)
				{
					SetFlowBreak(item, true);
					item.Width = item.Indented ? width : width + SystemInformation.MenuCheckSize.Width;
				}
			}
		}

		private sealed class MenuItemPanel : Panel
		{
			private readonly HandyCheckBox box;
			private readonly IntPtr hcursor;


			public bool Checked
			{
				get => box.Checked;
				set => box.Checked = value;
			}


			public bool Indented { get; private set; }


			public MenuItemPanel(CtxMenu item)
				: this(item.Name, item.ResID, Color.FromArgb(214, 166, 211))
			{
			}


			public MenuItemPanel(CtxMenuItem item)
				: this(item.Name, item.ResID, Color.Transparent)
			{
				Margin = new Padding(SystemInformation.MenuCheckSize.Width, 1, 1, 0);
				Indented = true;
			}


			private MenuItemPanel(string name, string resID, Color color)
			{
				using var graphics = Graphics.FromHwnd(IntPtr.Zero);
				var textSize = graphics.MeasureString(name, Font);
				Height = (int)(textSize.Height + 6);

				BackColor = color;

				box = new HandyCheckBox
				{
					Dock = DockStyle.Fill,
					Padding = new Padding(4, 2, 10, 2),
					Text = name
				};

				hcursor = Native.LoadCursor(IntPtr.Zero, Native.IDC_HAND);

				box.MouseEnter += (s, e) => { Native.SetCursor(hcursor); };
				box.MouseLeave += (s, e) => { box.Cursor = Cursors.Default; };

				// track by ResID, settings
				Tag = resID;

				Controls.Add(box);
			}
		}


		private sealed class HandyCheckBox : CheckBox
		{
			private readonly IntPtr hcursor;

			public HandyCheckBox()
			{
				Cursor = Cursors.Hand;
				hcursor = Native.LoadCursor(IntPtr.Zero, Native.IDC_HAND);
			}

			protected override void WndProc(ref Message m)
			{
				if (m.Msg == Native.WM_SETCURSOR && hcursor != IntPtr.Zero)
				{
					Native.SetCursor(hcursor);
					m.Result = IntPtr.Zero; // indicate handled
					return;
				}

				base.WndProc(ref m);
			}
		}
		#endregion Private classes


		private readonly MenuPanel menuPanel;


		public ContextMenuSheet(SettingsProvider provider) : base(provider)
		{
			InitializeComponent();

			Name = "ContextMenuSheet";
			Title = Resx.ContextMenuSheet_Title;

			if (NeedsLocalizing())
			{
				Localize(new string[]
				{
					"introBox"
				});
			}

			menuPanel = new MenuPanel
			{
				Dock = DockStyle.Fill
			};

			contentPanel.Controls.Add(menuPanel);

			var menus = CollectCommandMenus();
			menuPanel.AddMenus(menus);

			var settings = provider.GetCollection(Name);
			var items = settings.Get<XElement>("items");

			foreach (MenuItemPanel item in menuPanel.Controls)
			{
				if (item.Tag is string key)
				{
					key = key.Replace("_Label", string.Empty);
					if (items.Elements().Any(e => e.Value == key))
					{
						item.Checked = true;
					}
				}
			}
		}


		IEnumerable<CtxMenu> CollectCommandMenus()
		{
			var atype = typeof(CommandAttribute);

			var menus = typeof(AddIn).GetMethods()
				.Select(m => new
				{
					Method = m,
					Attribute = (CommandAttribute)m.GetCustomAttribute(atype)
				})
				.Where(o =>
					o.Method.Name.EndsWith("Cmd") &&
					o.Attribute != null &&
					!string.IsNullOrWhiteSpace(o.Attribute.Category)
				)
				.Select(o => new
				{
					o.Attribute.ResID,
					CategoryResID = $"{o.Attribute.Category}_Label",
					Name = Resx.ResourceManager.GetString(o.Attribute.ResID),
					Category = Resx.ResourceManager.GetString($"{o.Attribute.Category}_Label")
				})
				.GroupBy(c => new { Name = c.Category, ResID = c.CategoryResID })
				.Select(g => new CtxMenu
				{
					Name = string.Format(Resx.ContextMenuSheet_menu, g.Key.Name),
					ResID = g.Key.ResID,
					Commands = g.Select(c => new CtxMenuItem { Name = c.Name, ResID = c.ResID })
						.OrderBy(c => c.Name)
				})
				.OrderBy(m => m.Name);

			// add Proofing Language menu
			var codes = Office.GetEditingLanguages();
			if (codes != null && codes.Length > 1)
			{
				var list = menus.ToList();
				list.Add(new CtxMenu
				{
					Name = Resx.ResourceManager.GetString(ProofingResID),
					ResID = ProofingResID,
					Commands = new List<CtxMenuItem>()
				});

				menus = list.OrderBy(m => m.Name);
			}

			return menus;
		}


		public override bool CollectSettings()
		{
			var updated = false;

			var settings = provider.GetCollection(Name);
			var items = settings.Get<XElement>("items");
			items ??= new XElement("items");

			foreach (MenuItemPanel control in menuPanel.Controls)
			{
				var key = ((string)control.Tag).Replace("_Label", string.Empty);

				if (control.Checked)
				{
					if (!items.Elements().Any(e => e.Value == key))
					{
						items.Add(new XElement("item", key));
						updated = true;
					}
				}
				else
				{
					var item = items.Elements().FirstOrDefault(e => e.Value == key);
					if (item != null)
					{
						item.Remove();
						updated = true;
					}
				}
			}

			if (updated)
			{
				settings.Add(items);

				if (settings.Count > 0)
				{
					provider.SetCollection(settings);
				}
				else
				{
					provider.RemoveCollection(Name);
				}
			}

			return updated;
		}


		public static void UpgradeSettings(SettingsProvider provider)
		{
			/*
			 * TODO: Temporary
			 * Reader-makes-right to convert old button names to new names
			 * Released with 5.1.0
			 */

			var exchange = new Dictionary<string, string>
			{
				{ "ribDrawPlantUmlButton", "ribPlantUmlButton" },
				{ "ribInsertBoxButton", "ribInsertTextBoxButton" },
				{ "ribInsertCodeBlockButton", "ribInsertCodeBoxButton" },
				{ "ribInsertInfoBlockButton", "ribInsertInfoBoxButton" }
			};

			var collection = provider.GetCollection(nameof(ContextMenuSheet));
			var updated = false;

			exchange.ForEach(item =>
			{
				if (collection.Contains(item.Key))
				{
					collection.Remove(item.Key);
					collection.Add(item.Value, true);
					updated = true;
				}
			});

			/*
			 * TODO: Temporary
			 * Reader-makes-right to convert from indvidual elements like
			 * Released with 5.8.3
			 * 
			 *		<ribFooButton_Label>true</ribFooButton_Label>
			 *	
			 * to a more XML-conforming list of items like
			 * 
			 *		<items>
			 *		  <item>ribFooButton_Label</item>
			 *		  <item>ribBarButton_Label</item>
			 *		</items>
			 */

			if (!collection.Contains("items"))
			{
				var items = new XElement("items");
				collection.Keys.ToList().ForEach(key =>
				{
					items.Add(new XElement("item", key));
					collection.Remove(key);
				});

				collection.Add(items);
				updated = true;
			}

			if (updated)
			{
				provider.SetCollection(collection);
				provider.Save();
			}
		}
	}
}
