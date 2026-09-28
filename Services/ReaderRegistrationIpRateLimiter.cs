using System.Net;

namespace Project.Services;

public sealed class ReaderRegistrationIpRateLimiter(TimeProvider? timeProvider = null)
{
    private readonly TimeProvider clock = timeProvider ?? TimeProvider.System;
    private const int Limit = 3;
    private static readonly TimeSpan Window = TimeSpan.FromHours(1);
    private readonly object sync = new();
    private readonly Dictionary<string, Entry> entries = new(StringComparer.Ordinal);

    public bool TryAcquire(string ipAddress, out RegistrationLease lease)
    {
        if (IPAddress.TryParse(ipAddress, out var address))
        {
            if (address.IsIPv4MappedToIPv6) address = address.MapToIPv4();
            ipAddress = IPAddress.IsLoopback(address) ? IPAddress.Loopback.ToString() : address.ToString();
        }

        lock (sync)
        {
            var now = clock.GetUtcNow();
            if (!entries.TryGetValue(ipAddress, out var entry))
            {
                entry = new Entry();
                entries[ipAddress] = entry;
            }

            entry.RegistrationAttempts.RemoveAll(timestamp => now - timestamp >= Window);
            if (entry.RegistrationAttempts.Count + entry.PendingRegistrations >= Limit)
            {
                lease = null!;
                return false;
            }

            entry.PendingRegistrations++;
            lease = new RegistrationLease(this, ipAddress, now);
            return true;
        }
    }

    private void Complete(string ipAddress, bool succeeded, DateTimeOffset attemptedAtUtc)
    {
        lock (sync)
        {
            var entry = entries[ipAddress];
            entry.PendingRegistrations--;
            if (succeeded)
            {
                entry.RegistrationAttempts.Add(attemptedAtUtc);
            }
        }
    }

    private sealed class Entry
    {
        public List<DateTimeOffset> RegistrationAttempts { get; } = [];
        public int PendingRegistrations { get; set; }
    }

    public sealed class RegistrationLease : IDisposable
    {
        private readonly ReaderRegistrationIpRateLimiter owner;
        private readonly string ipAddress;
        private readonly DateTimeOffset attemptedAtUtc;
        private bool completed;

        internal RegistrationLease(ReaderRegistrationIpRateLimiter owner, string ipAddress, DateTimeOffset attemptedAtUtc)
        {
            this.owner = owner;
            this.ipAddress = ipAddress;
            this.attemptedAtUtc = attemptedAtUtc;
        }

        public void Commit()
        {
            if (completed) return;
            completed = true;
            owner.Complete(ipAddress, succeeded: true, attemptedAtUtc);
        }

        public void Dispose()
        {
            if (completed) return;
            completed = true;
            owner.Complete(ipAddress, succeeded: false, attemptedAtUtc);
        }
    }
}
