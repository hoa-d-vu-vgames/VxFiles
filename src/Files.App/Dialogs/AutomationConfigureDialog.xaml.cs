// Copyright (c) VxFiles contributors
// Licensed under the MIT License.

using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.Extensions.Logging;
using VxFiles.Automation.Abstractions;

namespace Files.App.Dialogs
{
	/// <summary>
	/// Where an Automation Package is given the programs it needs and the settings its actions declare.
	/// </summary>
	/// <remarks>
	/// Package-scoped and applied in one call: the rail's pages are views, not save units, so a partial save cannot
	/// exist and Cancel discards everything without a write.
	/// </remarks>
	public sealed partial class AutomationConfigureDialog : ContentDialog
	{
		private static ICommonDialogService CommonDialogService { get; } = Ioc.Default.GetRequiredService<ICommonDialogService>();

		private Func<AutomationPackageConfiguration, Task>? _apply;

		private FrameworkElement RootAppElement
			=> (FrameworkElement)MainWindow.Instance.Content;

		public AutomationConfigureDialogViewModel ViewModel
		{
			get => (AutomationConfigureDialogViewModel)DataContext;
			set => DataContext = value;
		}

		public AutomationConfigureDialog()
		{
			InitializeComponent();
		}

		/// <summary>
		/// Shows the dialog for one package and applies what the user saved.
		/// </summary>
		/// <remarks>
		/// Applying happens while the dialog is still up, so a refusal can be shown against the boxes that caused
		/// it rather than after everything the user typed has gone. Save is disabled for everything the dialog can
		/// see coming, so a failure here means something moved underneath it.
		/// </remarks>
		public async Task ShowAsync(
			AutomationPackageSnapshot package,
			Func<AutomationPackageConfiguration, Task> apply)
		{
			ArgumentNullException.ThrowIfNull(package);
			ArgumentNullException.ThrowIfNull(apply);

			ViewModel = new(package);
			_apply = apply;
			await this.TryShowAsync();
		}

		private async void ContentDialog_PrimaryButtonClick(ContentDialog sender, ContentDialogButtonClickEventArgs args)
		{
			if (_apply is not { } apply)
				return;

			var deferral = args.GetDeferral();
			try
			{
				ViewModel.Failure = string.Empty;
				await apply(ViewModel.BuildConfiguration());
			}
			catch (Exception exception)
			{
				// Keep the dialog open on the values the user entered. Closing would discard a configuration that
				// is one corrected box away from being applicable.
				args.Cancel = true;
				App.Logger.LogWarning(exception, "Automation package configuration could not be saved");
				ViewModel.Failure = Strings.AutomationConfigureSaveFailed.GetLocalizedResource();
			}
			finally
			{
				deferral.Complete();
			}
		}

		/// <summary>
		/// Browses for a <c>filePath</c> or <c>folderPath</c> setting's value.
		/// </summary>
		/// <remarks>
		/// In the code-behind rather than on the row, because which picker opens is a host decision and the row is
		/// a projection of the manifest's declaration.
		/// </remarks>
		private void SettingBrowse_Click(object sender, RoutedEventArgs e)
		{
			if (sender is not Button { Tag: AutomationConfigureSettingItem setting })
				return;

			// One call for both, because which of them it is differs only by a flag — this is the same picker the
			// rest of the app opens for a path.
			if (CommonDialogService.Open_FileOpenDialog(
				MainWindow.Instance.WindowHandle,
				setting.PicksFolder,
				[],
				Environment.SpecialFolder.MyComputer,
				out var path))
			{
				setting.TextValue = path;
			}
		}
	}
}
