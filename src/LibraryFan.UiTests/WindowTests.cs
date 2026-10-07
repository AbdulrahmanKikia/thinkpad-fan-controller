using Xunit;

namespace LibraryFan.UiTests;

public class WindowTests
{
    [Fact]
    public void Simulator_window_passes_the_safety_script()
    {
        Exception? error = null;
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
        Assert.Null(error?.ToString());
    }
}
