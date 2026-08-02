using UnityComdr.Models;

namespace UnityComdr.Editor;

/// <summary>
/// Task-level exclusive write lease (DESIGN §5.3). TTL expiry auto-releases.
/// Shared by headless host and live bridge server (logic duplicated in Unity package).
/// </summary>
public sealed class TaskLeaseManager
{
    private readonly Func<DateTimeOffset> _clock;
    private readonly object _gate = new();
    private string? _holder;
    private DateTimeOffset _expiresAt;

    public TaskLeaseManager(Func<DateTimeOffset>? clock = null)
    {
        _clock = clock ?? (() => DateTimeOffset.UtcNow);
    }

    public LeaseInfo GetLease()
    {
        lock (_gate)
        {
            PurgeExpired_NoLock();
            return Snapshot_NoLock();
        }
    }

    public LeaseInfo Acquire(string agentId, double ttlSeconds)
    {
        if (string.IsNullOrWhiteSpace(agentId))
            throw new ArgumentException("agentId required", nameof(agentId));
        if (ttlSeconds <= 0)
            throw new ArgumentException("ttlSeconds must be > 0", nameof(ttlSeconds));

        lock (_gate)
        {
            PurgeExpired_NoLock();
            var now = _clock();
            if (_holder != null &&
                !_holder.Equals(agentId, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    $"busy holder={_holder} expiresAt={_expiresAt:O}");
            }

            _holder = agentId;
            _expiresAt = now.AddSeconds(ttlSeconds);
            return Snapshot_NoLock();
        }
    }

    public bool TryAcquire(string agentId, double ttlSeconds, out LeaseInfo info, out string? error)
    {
        try
        {
            info = Acquire(agentId, ttlSeconds);
            error = null;
            return true;
        }
        catch (Exception ex)
        {
            info = GetLease();
            error = ex.Message;
            return false;
        }
    }

    public bool Release(string agentId, out string? error)
    {
        if (string.IsNullOrWhiteSpace(agentId))
        {
            error = "agentId required";
            return false;
        }

        lock (_gate)
        {
            PurgeExpired_NoLock();
            if (_holder == null)
            {
                error = "no lease held";
                return false;
            }
            if (!_holder.Equals(agentId, StringComparison.OrdinalIgnoreCase))
            {
                error = $"not_holder holder={_holder}";
                return false;
            }
            _holder = null;
            _expiresAt = default;
            error = null;
            return true;
        }
    }

    /// <summary>
    /// Authorize a write. When <paramref name="requireHeld"/> is true (act.input),
    /// caller must currently hold the lease. Otherwise free lease allows any writer;
    /// held lease allows only the holder.
    /// </summary>
    public LeaseAuthorization AuthorizeWrite(string? agentId, bool requireHeld = false)
    {
        lock (_gate)
        {
            PurgeExpired_NoLock();
            if (_holder == null)
            {
                if (requireHeld)
                    return LeaseAuthorization.MissingLease();
                return LeaseAuthorization.Ok();
            }

            if (!string.IsNullOrWhiteSpace(agentId) &&
                _holder.Equals(agentId, StringComparison.OrdinalIgnoreCase))
                return LeaseAuthorization.Ok(_holder, _expiresAt);

            return LeaseAuthorization.Busy(_holder, _expiresAt);
        }
    }

    private void PurgeExpired_NoLock()
    {
        if (_holder == null) return;
        if (_clock() >= _expiresAt)
        {
            _holder = null;
            _expiresAt = default;
        }
    }

    private LeaseInfo Snapshot_NoLock() => new()
    {
        Holder = _holder,
        ExpiresAt = _holder == null ? null : _expiresAt
    };
}
