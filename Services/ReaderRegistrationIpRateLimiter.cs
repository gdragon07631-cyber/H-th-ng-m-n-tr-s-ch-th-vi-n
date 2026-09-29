namespace Project.Services;

public sealed class ReaderRegistrationIpRateLimiter
{
    private const int Limit = 3;
    private static readonly TimeSpan Window = TimeSpan.FromHours(1);
    private readonly object sync = new();
    private readonly Dictionary<string, Entry> entries = new(StringComparer.Ordinal);

    public bool TryAcquire(string ipAddress, out RegistrationLease lease)
    {
        lock (sync)
        {
            var now = DateTimeOffset.UtcNow;
            if (!entries.TryGetValue(ipAddress, out var entry))
            {
                entry = new Entry();
                entries[ipAddress] = entry;
            }

            entry.SuccessfulRegistrations.RemoveAll(timestamp => now - timestamp >= Window);
            if (entry.SuccessfulRegistrations.Count + entry.PendingRegistrations >= Limit)
            {
                lease = null!;
                return false;
            }

            entry.PendingRegistrations++;
            lease = new RegistrationLease(this, ipAddress);
            return true;
        }
    }

    private void Complete(string ipAddress, bool succeeded)
    {
        lock (sync)
        {
            var entry = entries[ipAddress];
            entry.PendingRegistrations--;
            if (succeeded)
            {
                entry.SuccessfulRegistrations.Add(DateTimeOffset.UtcNow);
            }
        }
    }

    private sealed class Entry
    {
        public List<DateTimeOffset> SuccessfulRegistrations { get; } = [];
        public int PendingRegistrations { get; set; }
    }

    public sealed class RegistrationLease : IDisposable
    {
        private readonly ReaderRegistrationIpRateLimiter owner;
        private readonly string ipAddress;
        private bool completed;

        internal RegistrationLease(ReaderRegistrationIpRateLimiter owner, string ipAddress)
        {
            this.owner = owner;
            this.ipAddress = ipAddress;
        }

        public void Commit()
        {
            if (completed) return;
            completed = true;
            owner.Complete(ipAddress, succeeded: true);
        }

        public void Dispose()
        {
            if (completed) return;
            completed = true;
            owner.Complete(ipAddress, succeeded: false);
        }
    }
}
