using System.Windows.Threading;
using Dockyard.Application;

namespace Dockyard.App;

/// <summary><see cref="IScheduler"/> on the WPF dispatcher.</summary>
internal sealed class WpfScheduler(Dispatcher dispatcher) : IScheduler
{
    public void Post(Action action) => dispatcher.BeginInvoke(action, DispatcherPriority.Input);

    public IRestartableTimer CreateTimer(TimeSpan delay, Action tick) => new Timer(dispatcher, delay, tick);

    private sealed class Timer : IRestartableTimer
    {
        private readonly DispatcherTimer _timer;

        public Timer(Dispatcher dispatcher, TimeSpan delay, Action tick)
        {
            _timer = new DispatcherTimer(DispatcherPriority.Normal, dispatcher) { Interval = delay };
            _timer.Tick += (_, _) =>
            {
                _timer.Stop();
                tick();
            };
        }

        public void Restart()
        {
            _timer.Stop();
            _timer.Start();
        }

        public void Stop() => _timer.Stop();
    }
}
