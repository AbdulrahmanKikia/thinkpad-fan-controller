using System.Windows.Threading;
using LibraryFan;
using Xunit;

namespace LibraryFan.UiTests;

public class WindowTests
{
    [Fact]
    public void Simulator_window_trips_and_refuses_until_it_cools()
    {
        Exception? error = null;
        var thread = new Thread(() =>
        {
            try
            {
                App.HeadlessHost = true;
                var app = new App
                {
                    ShutdownMode = System.Windows.ShutdownMode.OnExplicitShutdown
                };
                app.Dispatcher.InvokeAsync(async () =>
                {
                    try
                    {
                        var window = new MainWindow(new SimulatedEc(), simulate: true, selfTest: true);
                        window.Show();
                        if (!await window.RunSelfTestAsync())
                            error = new InvalidOperationException("The simulator window did not complete the fan handoff.");
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
