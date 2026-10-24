using XRBrowser.Models;

namespace XRBrowser.Services;

public sealed class SessionService
{
    private readonly JsonStore<SessionState> _store = new(AppPaths.SessionFile);
    private SessionState? _pending;

    public SessionState Load() => _store.Load();

    public void QueueSave(SessionState state) => _pending = state;

    public void Save(SessionState state)
    {
        _pending = null;
        _store.Save(state);
    }

    public void Flush()
    {
        if (_pending is not null) Save(_pending);
    }
}
