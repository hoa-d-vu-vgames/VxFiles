// Copyright (c) VxFiles contributors
// Licensed under the MIT License.

using VxFiles.Automation.Abstractions;

namespace Files.App.Data.Items
{
	/// <summary>
	/// One entry in the Configure dialog's rail, and the page it shows.
	/// </summary>
	/// <remarks>
	/// A rail is navigation, not an inventory: an action with nothing to configure gets no entry, so a package with
	/// five actions and settings on two of them shows three entries rather than six.
	///
	/// <para>
	/// Pages are views, not save units. The whole dialog is applied in one call, so nothing here decides when
	/// anything is written and moving between pages costs nothing.
	/// </para>
	/// </remarks>
	public sealed partial class AutomationConfigurePageItem : ObservableObject
	{
		private AutomationConfigurePageItem(string title, AutomationActionLocalId? actionId)
		{
			Title = title;
			ActionId = actionId;
		}

		/// <summary>
		/// The page carrying every external tool the package declares. Exactly one package has one, and only when
		/// it declares a tool at all.
		/// </summary>
		public static AutomationConfigurePageItem Programs(IEnumerable<AutomationConfigureToolItem> tools)
		{
			var page = new AutomationConfigurePageItem(Strings.AutomationConfigurePrograms.GetLocalizedResource(), null);
			foreach (var tool in tools)
				page.Tools.Add(tool);

			return page;
		}

		public static AutomationConfigurePageItem ForAction(
			AutomationActionSnapshot action,
			IEnumerable<AutomationConfigureSettingItem> settings)
		{
			var page = new AutomationConfigurePageItem(action.DisplayName, action.Id.LocalId);
			foreach (var setting in settings)
				page.Settings.Add(setting);

			return page;
		}

		public string Title { get; }

		/// <summary>
		/// Gets the action this page configures, or <see langword="null"/> for the Programs page — whose tools
		/// belong to the package rather than to any one action.
		/// </summary>
		public AutomationActionLocalId? ActionId { get; }

		public bool IsPrograms => ActionId is null;

		public ObservableCollection<AutomationConfigureToolItem> Tools { get; } = [];

		public ObservableCollection<AutomationConfigureSettingItem> Settings { get; } = [];

		/// <summary>
		/// Gets whether this page holds something Save is waiting on, which the rail marks.
		/// </summary>
		/// <remarks>
		/// The marker is the point of the rail carrying state at all: without it a refused path on a page the user
		/// has navigated away from disables Save with nothing on screen to explain why.
		/// </remarks>
		public bool HasError => Tools.Any(tool => tool.IsRefused);

		public void RefreshError() => OnPropertyChanged(nameof(HasError));
	}
}
