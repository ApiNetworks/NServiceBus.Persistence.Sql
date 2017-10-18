﻿using System.IO;
using System.Security.Cryptography;
using System.Text;
using NServiceBus.Persistence.Sql.ScriptBuilder;

class PostgreSqlSagaScriptWriter : ISagaScriptWriter
{
    TextWriter writer;
    SagaDefinition saga;

    public PostgreSqlSagaScriptWriter(TextWriter textWriter, SagaDefinition saga)
    {
        writer = textWriter;
        this.saga = saga;
    }

    public void Initialize()
    {
    }

    public void WriteTableNameVariable()
    {
    }


    public void AddProperty(CorrelationProperty correlationProperty)
    {
        var columnType = PostgreSqlCorrelationPropertyTypeConverter.GetColumnType(correlationProperty.Type);
        var name = correlationProperty.Name;

        writer.Write($@"
        script = 'alter table public.""' || tableNameNonQuoted || '"" add column if not exists ""Correlation_{name}"" {columnType}';
        execute script;
");
    }

    public void VerifyColumnType(CorrelationProperty correlationProperty)
    {
        var columnType = PostgreSqlCorrelationPropertyTypeConverter.GetColumnType(correlationProperty.Type);
        var name = correlationProperty.Name;
        writer.Write($@"
        columnType := (
            select data_type || ' ' || coalesce(' (' || character_maximum_length || ')', '')
            from information_schema.columns
            where
            table_name = 'tableNameNonQuoted' and
            column_name = 'Correlation_{name}'
        );
        if columnType <> '{columnType}' then
            raise exception 'Incorrect data type for Correlation_{name}. Expected {columnType} got %', columnType;
        end if;
");
    }

    public void WriteCreateIndex(CorrelationProperty correlationProperty)
    {
        var indexName = CreateSagaIndexName(saga.TableSuffix, correlationProperty.Name);

        writer.Write($@"
        script = 'create unique index if not exists ""' || tablePrefix || '{indexName}"" on public.""' || tableNameNonQuoted || '"" using btree (""Correlation_{correlationProperty.Name}"" asc);';
        execute script;"
);
    }

    public void WritePurgeObsoleteIndex()
    {
        // Dropping the column with the index attached removes the index as well
    }

    public void WritePurgeObsoleteProperties()
    {
    }


    public void WriteCreateTable()
    {
        var sagaName = saga.TableSuffix.Replace(' ', '_');
        writer.Write($@"
create or replace function pg_temp.create_saga_table_{sagaName}(tablePrefix varchar)
    returns integer as
    $body$
    declare
        tableNameNonQuoted varchar;
        script text;
        count int;
        columnType varchar;
    begin
        tableNameNonQuoted := tablePrefix || '{saga.TableSuffix}';
        script = 'create table if not exists public.""' || tableNameNonQuoted || '""
(
    ""Id"" uuid not null,
    ""Metadata"" text not null,
    ""Data"" jsonb not null,
    ""PersistenceVersion"" character varying(23),
    ""SagaTypeVersion"" character varying(23),
    ""Concurrency"" int not null,
    primary key(""Id"")
);';
        execute script;
");
    }
    public void CreateComplete()
    {
        var sagaName = saga.TableSuffix.Replace(' ', '_');
        writer.Write($@"
        return 0;
    end;
    $body$
language 'plpgsql';

select pg_temp.create_saga_table_{sagaName}(@tablePrefix);
");
    }

    public void WriteDropTable()
    {
        var sagaName = saga.TableSuffix.Replace(' ', '_');
        writer.Write(
$@"create or replace function pg_temp.drop_saga_table_{sagaName}(tablePrefix varchar)
    returns integer as
    $body$
    declare
        tableNameNonQuoted varchar;
        dropTable text;
    begin
        tableNameNonQuoted := tablePrefix || '{saga.TableSuffix}';
        dropTable = 'drop table if exists public.""' || tableNameNonQuoted || '"";';
        execute dropTable;
        return 0;
    end;
    $body$
    language 'plpgsql';

select pg_temp.drop_saga_table_{sagaName}(@tablePrefix);
");
    }

    // Generates an index name like "idxc_66462BF46C9DCB70FD257D" based on
    // SHA1 hash of saga name and correlation property name
    string CreateSagaIndexName(string sagaName, string correlationPropertyName)
    {
        var sb = new StringBuilder("idxc_", 30);
        var clearText = Encoding.UTF8.GetBytes($"{sagaName}/{correlationPropertyName}");
        using (var sha1 = SHA1.Create())
        {
            var hashBytes = sha1.ComputeHash(clearText);
            // SHA1 hash contains 20 bytes, but only have space in 30 char index name for 11 bytes => 22 chars
            for (var i = 0; i < 20; i++)
            {
                sb.Append(hashBytes[i].ToString("X2"));
            }
        }
        return sb.ToString();
    }
}