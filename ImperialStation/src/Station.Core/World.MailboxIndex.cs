using System;
using System.Collections.Generic;
using Station.Contracts;

namespace Station.Core
{
    public sealed partial class World
    {
        private readonly SortedDictionary<(RecipientKind, long), SortedSet<long>> unreadMail = new SortedDictionary<(RecipientKind, long), SortedSet<long>>();
        private IEnumerable<long> UnreadMessages(RecipientKind kind, long account) => unreadMail.TryGetValue((kind, account), out var ids) ? ids : (IEnumerable<long>)Array.Empty<long>();
        private void IndexUnreadMessage(MessageSnapshot message)
        {
            var kind = message.Draft.RecipientKind;
            if (message.Stage != MessageStage.DeliveredToMailbox || !(kind is RecipientKind.Account or RecipientKind.RecoveryAccount)) return;
            var key = (kind, message.Draft.Recipient.Value);
            if (!unreadMail.TryGetValue(key, out var ids)) unreadMail.Add(key, ids = new SortedSet<long>());
            ids.Add(message.Id);
        }
        private void RemoveUnreadMessage(MessageSnapshot message)
        {
            var key = (message.Draft.RecipientKind, message.Draft.Recipient.Value);
            if (!unreadMail.TryGetValue(key, out var ids)) return;
            ids.Remove(message.Id); if (ids.Count == 0) unreadMail.Remove(key);
        }
    }
}
