namespace FileCat.Platform.Windows.Html;

/// <summary>
/// Release issue I648: WebView2 sometimes never reports that a page's navigation completed, though every request the
/// page made of FileCat was answered. The page then stayed blank as far as the viewer knew, with Refresh disabled.
/// This follows the page being shown, from <see cref="WebView2Page.Show"/> on, and decides what to do once it has made
/// no progress for <see cref="Quiet"/> while none of its own requests is still being answered:
/// <list type="bullet">
/// <item>its page process ended: it is navigated once more, and if that brings no new navigation or the new one's
/// process ends too, the page could not be shown;</item>
/// <item>its content loaded (WebView2's DOMContentLoaded): it counts as drawn;</item>
/// <item>it never began to load (no ContentLoading): it is navigated once more;</item>
/// <item>otherwise, or after that one retry: nothing more; the viewer keeps Refresh available.</item>
/// </list>
/// A completion that does come is passed on as WebView2 gives it, failures included, unless it belongs to a navigation
/// that a newer one replaced, or it is a success for a page that already counts as drawn. Not thread-safe: the view
/// calls it on its UI thread.
/// </summary>
internal sealed class PageCompletionWatch(Func<long> milliseconds, TimeSpan quiet)
{
    public enum Decision
    {
        /// <summary>Nothing to do now; <see cref="Check"/> says when to look again, if at all.</summary>
        Wait,
        /// <summary>Report the page as drawn: its content loaded, and no completion came.</summary>
        TreatAsDrawn,
        /// <summary>Navigate to the page once more: it never began to load, or its page process ended.</summary>
        NavigateAgain,
        /// <summary>Report that the page could not be shown: its page process ended again after the retry.</summary>
        Fail,
        /// <summary>The retry did not help either: act again only once the page makes progress.</summary>
        GiveUp,
    }

    private const int SupersededKept = 64;
    private readonly Queue<ulong> _supersededOrder = new();
    private readonly HashSet<ulong> _superseded = [];
    private ulong? _navigation;
    private bool _active, _committed, _contentLoaded, _reported, _retried, _resting, _processEnded;
    private int _pending;
    private long _lastProgress;

    public TimeSpan Quiet { get; } = quiet;

    /// <summary>Whether a page is being watched: shown, and neither completed nor counted as drawn.</summary>
    public bool Active => _active;

    /// <summary>A page is to be shown: whatever WebView2 was doing is old news, and the new page is watched from now on.</summary>
    public void Begin()
    {
        Supersede();
        _active = true;
        _reported = _retried = _resting = _processEnded = false;
        _lastProgress = milliseconds();
    }

    /// <summary>The view reported the page itself (it could not load it again): stop watching it.</summary>
    public void Stop()
    {
        _active = false;
        _reported = true;
    }

    /// <summary>Something happened that moves the page along (the view starting, a request, an event): the quiet restarts.</summary>
    public void Progress()
    {
        _lastProgress = milliseconds();
        _resting = false;
    }

    /// <summary>
    /// WebView2 accepted a navigation to the page's address (FileCat's own, or one the page made within its folder): it
    /// is the one watched from now on. A redirect keeps its navigation's number.
    /// </summary>
    public void Started(ulong navigation)
    {
        if (_navigation != navigation)
        {
            Supersede();
            _navigation = navigation;
            // A new navigation gets a page process of its own; an ended one no longer stands in its way.
            _processEnded = false;
        }
        Progress();
    }

    /// <summary>WebView2's ContentLoading: the navigation committed, and its document is arriving.</summary>
    public void Committed(ulong navigation)
    {
        if (navigation == _navigation) _committed = true;
        Progress();
    }

    /// <summary>WebView2's DOMContentLoaded: the document is parsed, and it is what the page shows.</summary>
    public void ContentLoaded(ulong navigation)
    {
        if (navigation == _navigation) _committed = _contentLoaded = true;
        Progress();
    }

    /// <summary>One of the page's own requests reached FileCat.</summary>
    public void RequestStarted()
    {
        _pending++;
        Progress();
    }

    /// <summary>That request was answered (its deferral completed, or it could not be).</summary>
    public void RequestEnded()
    {
        if (_pending > 0) _pending--;
        Progress();
    }

    /// <summary>The page's renderer or the browser ended: what it drew is gone.</summary>
    public void ProcessEnded()
    {
        if (_active) _processEnded = true;
        Progress();
    }

    /// <summary>What the view does with a completion WebView2 reported.</summary>
    public enum Report
    {
        /// <summary>Pass it on: it is the page's own, or one the view never replaced.</summary>
        PassOn,
        /// <summary>Drop it: a newer navigation replaced the one it belongs to.</summary>
        Superseded,
        /// <summary>Drop it: a success for a page that already counts as drawn, which was reported then.</summary>
        AlreadyDrawn,
    }

    /// <summary>WebView2 reported a completion (not a cancellation): whether the view passes it on.</summary>
    public Report Completed(ulong navigation, bool success)
    {
        if (_superseded.Contains(navigation)) return Report.Superseded;
        // Not the navigation watched (none was accepted that would have replaced it): passed on, as it always was.
        if (_navigation is { } current && navigation != current) return Report.PassOn;
        bool reported = _reported;
        _active = false;
        _reported = true;
        return success && reported ? Report.AlreadyDrawn : Report.PassOn;
    }

    /// <summary>What to do now; <paramref name="again"/> is when to check next (null: on the next progress only).</summary>
    public Decision Check(bool viewReady, out TimeSpan? again)
    {
        again = null;
        if (!_active || _resting) return Decision.Wait;
        long quietFor = milliseconds() - _lastProgress;
        // The browser is still starting, or the page waits for FileCat's own answer (a slow disk, a large file): that
        // is not WebView2 losing track of it.
        if (!viewReady || _pending > 0)
        {
            again = Quiet;
            return Decision.Wait;
        }
        if (quietFor < (long)Quiet.TotalMilliseconds)
        {
            again = Quiet - TimeSpan.FromMilliseconds(quietFor);
            return Decision.Wait;
        }
        if (_processEnded)
        {
            if (_retried)
            {
                Stop();
                return Decision.Fail;
            }
            Retry();
            again = Quiet;
            return Decision.NavigateAgain;
        }
        if (_contentLoaded)
        {
            Stop();
            return Decision.TreatAsDrawn;
        }
        if (!_committed && !_retried)
        {
            Retry();
            again = Quiet;
            return Decision.NavigateAgain;
        }
        _resting = true;
        return Decision.GiveUp;
    }

    private void Retry()
    {
        Supersede();
        _retried = true;
        _lastProgress = milliseconds();
    }

    private void Supersede()
    {
        if (_navigation is { } previous && _superseded.Add(previous))
        {
            _supersededOrder.Enqueue(previous);
            if (_supersededOrder.Count > SupersededKept) _superseded.Remove(_supersededOrder.Dequeue());
        }
        _navigation = null;
        _committed = _contentLoaded = _reported = false;
    }
}
