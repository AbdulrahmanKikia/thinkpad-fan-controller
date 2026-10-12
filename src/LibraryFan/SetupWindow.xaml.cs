using System.Windows;
using System.Windows.Controls;

namespace LibraryFan;

/// <summary>
/// Asked once, on the first hardware launch. Continuing, or closing the window, keeps the
/// computer and the checkbox that were showing. The panel itself is not this window.
/// </summary>
public partial class SetupWindow : Window
{
    private readonly MachineIdentity _machine;

    public SetupWindow(bool startWithWindows, string? profileId = null, MachineIdentity? machine = null)
    {
        InitializeComponent();
        _machine = machine ?? new MachineIdentity("", "");
        StartWithWindows = startWithWindows;
        StartWithWindowsBox.IsChecked = startWithWindows;
        StartWithWindowsBox.Checked += (_, _) => StartWithWindows = true;
        StartWithWindowsBox.Unchecked += (_, _) => StartWithWindows = false;

        MachineBox.ItemsSource = KnownMachines.All;
        MachineBox.SelectedItem = KnownMachines.Find(profileId) ?? KnownMachines.Match(_machine) ?? KnownMachines.All[0];
        MachineBox.SelectionChanged += MachineChanged;
        ShowMatch();
    }

    public bool StartWithWindows { get; private set; }

    public string? ProfileId => (MachineBox.SelectedItem as KnownMachine)?.Id;

    private void MachineChanged(object sender, SelectionChangedEventArgs e) => ShowMatch();

    private void ShowMatch() =>
        MatchNote.Text = KnownMachines.Explain(MachineBox.SelectedItem as KnownMachine, _machine);

    private void ContinueClick(object sender, RoutedEventArgs e) => Close();

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        WindowFrame.Apply(this);
    }
}
