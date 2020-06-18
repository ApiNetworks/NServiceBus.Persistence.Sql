﻿using System.Data.Common;
using System.Threading.Tasks;
using NServiceBus;
using NServiceBus.Extensibility;
using NServiceBus.Outbox;

class PessimisticOutboxBehavior : OutboxBehavior
{
    SqlDialect sqlDialect;
    OutboxCommands outboxCommands;

    public PessimisticOutboxBehavior(SqlDialect sqlDialect, OutboxCommands outboxCommands)
    {
        this.sqlDialect = sqlDialect;
        this.outboxCommands = outboxCommands;
    }

    public override async Task Begin(string messageId, DbConnection connection, DbTransaction transaction)
    {
        using (var command = sqlDialect.CreateCommand(connection))
        {
            command.CommandText = outboxCommands.PessimisticBegin;
            command.Transaction = transaction;
            command.AddParameter("MessageId", messageId);
            command.AddParameter("PersistenceVersion", StaticVersions.PersistenceVersion);
            await command.ExecuteNonQueryEx().ConfigureAwait(false);
        }
    }

    public override async Task Complete(OutboxMessage outboxMessage, DbConnection connection, DbTransaction transaction, ContextBag context)
    {
        using (var command = sqlDialect.CreateCommand(connection))
        {
            command.CommandText = outboxCommands.PessimisticComplete;
            command.Transaction = transaction;
            command.AddParameter("MessageId", outboxMessage.MessageId);
            command.AddJsonParameter("Operations", Serializer.Serialize(outboxMessage.TransportOperations.ToSerializable()));
            await command.ExecuteNonQueryEx().ConfigureAwait(false);
        }
    }
}