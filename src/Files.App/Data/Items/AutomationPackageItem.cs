// Copyright (c) VxFiles contributors
// Licensed under the MIT License.

using System.Collections.Immutable;
using VxFiles.Automation.Abstractions;

namespace Files.App.Data.Items
{
	/// <summary>
	/// One Automation Package root in the Tools TreeView, holding its Automation Action children.
	/// </summary>
	public sealed partial class AutomationPackageItem : ObservableObject
	{
		private readonly AutomationPackageSnapshot _snapshot;

		// Held only to pass on to the action rows that ShowActions rebuilds; this type never calls it.
		private readonly Func<AutomationActionItem, Task> _run;

		private bool _isExpanded;

		public AutomationPackageItem(AutomationPackageSnapshot snapshot, Func<AutomationActionItem, Task> run)
		{
			ArgumentNullException.ThrowIfNull(snapshot);
			ArgumentNullException.ThrowIfNull(run);

			_snapshot = snapshot;
			_run = run;
			Diagnostics = string.Join(Environment.NewLine, snapshot.Diagnostics);
			HealthLabel = DescribeHealth(snapshot);
			ShowActions(snapshot.Actions);
		}

		/// <summary>
		/// Gets the catalog snapshot this row was built from, including the actions a filter is hiding.
		/// </summary>
		public AutomationPackageSnapshot Snapshot => _snapshot;

		public string Id => _snapshot.Id.Value;

		public string DisplayName => _snapshot.DisplayName;

		public string PackageVersion => _snapshot.PackageVersion;

		public string Description => _snapshot.Description;

		public bool HasDescription => !string.IsNullOrWhiteSpace(_snapshot.Description);

		/// <summary>
		/// Gets the package's own availability, qualified by how many of its actions survived validation.
		/// </summary>
		public string HealthLabel { get; }

		public string Diagnostics { get; }

		public bool HasDiagnostics => Diagnostics.Length is not 0;

		/// <summary>
		/// Gets the action rows currently shown, which is a subset of the package's actions while a filter
		/// is applied.
		/// </summary>
		public ObservableCollection<AutomationActionItem> Actions { get; } = [];

		public bool IsExpanded
		{
			get => _isExpanded;
			set => SetProperty(ref _isExpanded, value);
		}

		/// <summary>
		/// Replaces the visible action rows, keeping this root's identity and health untouched.
		/// </summary>
		public void ShowActions(ImmutableArray<AutomationActionSnapshot> actions)
		{
			Actions.Clear();
			foreach (var action in actions)
				Actions.Add(new AutomationActionItem(action, _run));
		}

		/// <summary>
		/// Always counts every action the package declares, so filtering out healthy siblings cannot make a
		/// package look broken.
		/// </summary>
		/// <remarks>
		/// A package that is short of some of its actions but not all of them is described by the count rather
		/// than by its own verdict. One action needing configuration makes the whole package need it, and
		/// "Needs configuration" over a package whose other three actions run reads as though none of them did —
		/// the row beneath gives the reason, and the count is what the root is for.
		/// </remarks>
		private static string DescribeHealth(AutomationPackageSnapshot snapshot)
		{
			// Neither is about individual actions: a package that failed validation declares none that survived,
			// and a run that could not resolve a dependency says nothing about which actions needed it.
			if (snapshot.Availability is AutomationAvailability.Disabled or AutomationAvailability.MissingDependency)
				return snapshot.Availability.ToLabel();

			var available = snapshot.Actions.Count(action => action.Availability is AutomationAvailability.Available);
			if (available is 0 && snapshot.Availability is AutomationAvailability.NeedsConfiguration)
				return snapshot.Availability.ToLabel();

			if (available == snapshot.Actions.Length)
				return AutomationAvailability.Available.ToLabel();

			return string.Format(
				Strings.AutomationToolsActionsAvailable.GetLocalizedResource(),
				available,
				snapshot.Actions.Length);
		}
	}
}
