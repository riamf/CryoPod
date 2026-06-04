using System;

namespace CryoPod.ViewModels
{
    public sealed class SuspendedGameViewModel
    {
        public SuspendedGameViewModel(Guid id, string name, DateTimeOffset suspendedAt)
        {
            Id = id;
            Name = name;
            SuspendedAt = suspendedAt;
            SuspendedSince = $"Paused {suspendedAt.LocalDateTime:g}";
        }

        public Guid Id { get; }

        public string Name { get; }

        public DateTimeOffset SuspendedAt { get; }

        public string SuspendedSince { get; }
    }
}
