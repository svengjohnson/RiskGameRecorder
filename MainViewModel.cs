
using System;
using System.Threading.Tasks;
using System.Windows.Threading;
using RiskGameRecorder.Memory;
using RiskGameRecorder.Recording;

namespace RiskGameRecorder;

public sealed class MainViewModel
{
    public GameRecorderViewModel RecorderInfo { get; } = new();

    private readonly DispatcherTimer _timer = new();
    private readonly GameRecorder    _recorder = new();
    private GameMemoryReader? _reader;
    private bool _refreshing;
    private DateTime? _gameStartTime;

    public MainViewModel()
    {
        _timer.Interval = TimeSpan.FromMilliseconds(50);
        _timer.Tick += async (_, __) => await RefreshAsync();
        _timer.Start();
    }

    async Task RefreshAsync()
    {
        if (_refreshing) return;
        _refreshing = true;
        try { await RefreshCoreAsync(); }
        finally { _refreshing = false; }
    }

    async Task RefreshCoreAsync()
    {
        if (_reader == null)
        {
            try { _reader = await Task.Run(() => new GameMemoryReader()); }
            catch
            {
                _gameStartTime = null;
                RecorderInfo.Update(_recorder.RecorderStatus, "—", null, "", "", null);
                return;
            }
        }

        if (!_reader.IsAlive)
        {
            _recorder.HandleProcessDied();
            _reader = null;
            _gameStartTime = null;
            RecorderInfo.Update(_recorder.RecorderStatus, "—", null, "", "", null);
            return;
        }

        FullMemorySnapshot snap;
        try { snap = await Task.Run(() => _reader.ReadFullSnapshot()); }
        catch
        {
            _recorder.HandleProcessDied();
            _reader = null;
            _gameStartTime = null;
            RecorderInfo.Update(_recorder.RecorderStatus, "—", null, "", "", null);
            return;
        }

        var readerRef = _reader;
        _ = Task.Run(() => _recorder.ProcessSnapshot(snap, readerRef));

        var state             = snap.State;
        var round             = snap.Round;
        var currentPlayerName = snap.CurrentPlayerName;
        var gameId            = snap.GameId;

        if (state == GameState.InGame)
        {
            _gameStartTime ??= DateTime.UtcNow;
            var elapsed = DateTime.UtcNow - _gameStartTime.Value;
            var timeStr = elapsed.TotalHours >= 1
                ? elapsed.ToString(@"h\:mm\:ss")
                : elapsed.ToString(@"mm\:ss");
            RecorderInfo.Update(_recorder.RecorderStatus, timeStr, round, currentPlayerName, gameId, _recorder.CurrentConfig);
        }
        else
        {
            _gameStartTime = null;
            RecorderInfo.Update(_recorder.RecorderStatus, "—", null, currentPlayerName, gameId, _recorder.CurrentConfig);
        }
    }
}
