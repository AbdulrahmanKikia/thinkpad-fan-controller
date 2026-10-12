using System.IO;
using Xunit;

namespace LibraryFan.UiTests;

public class WindowTests
{
    [Fact]
    public void Simulator_window_passes_the_safety_script()
    {
        Exception? error = null;
        var setupPath = Path.Combine(Path.GetTempPath(), "library-fan-setup.png");
        var thread = new Thread(() =>
        {
            try
            {
                App.HeadlessHost = true;
                var app = new App { ShutdownMode = System.Windows.ShutdownMode.OnExplicitShutdown };
                app.Dispatcher.InvokeAsync(async () =>
                {
                    try
                    {
                        // One Application per process, so the setup window is rendered on this
                        // same dispatcher before the safety script.
                        var setup = new SetupWindow(false, null, new MachineIdentity("20TH000XUS", "N2VET41W (1.26)"));
                        setup.ContentRendered += (_, _) => WindowSnapshot.Save(setup, setupPath);
                        setup.Show();
                        setup.Close();

                        var window = new MainWindow(new SimulatedEc(), simulate: true, selfTest: true);
                        window.Show();
                        if (!await SelfTest.RunAsync(window))
                            error = new InvalidOperationException($"The self-test failed: {window.Latest?.Reason}");
                        window.Close();
                    }
                    catch (Exception ex)
                    {
                        error = ex;
                    }
                    finally
                    {
                        app.Shutdown();
                    }
                });
                app.Run();
            }
            catch (Exception ex)
            {
                error = ex;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(30)), "The window test did not finish.");
        Assert.Null(error);
        Assert.True(File.Exists(setupPath) && new FileInfo(setupPath).Length > 1000);
    }
}
