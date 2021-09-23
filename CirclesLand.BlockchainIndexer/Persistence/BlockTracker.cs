using System;
using Dapper;
using Nethereum.BlockchainProcessing.BlockStorage.Entities.Mapping;
using Nethereum.RPC.Eth.DTOs;
using Npgsql;

namespace CirclesLand.BlockchainIndexer.Persistence
{
    public class BlockTracker
    {
        public static long GetLastValidBlock(NpgsqlConnection connection)
        {
            var lastKnownBlock = connection.QuerySingleOrDefault<long?>(
                @"with a as (
                                select distinct block_no
                                from requested_blocks
                                order by block_no
                            ), b as (
                                select distinct number
                                from block
                                order by number
                            ), c as (
                                select a.block_no as requested, b.number as actual
                                from a
                                left join b on a.block_no = b.number
                                order by a.block_no
                            )
                            select min(c.requested) - 1 as last_correctly_imported_block
                            from c
                            where actual is null;") ?? 12529458L;

            return lastKnownBlock;
        }

        public static void AddRequested(NpgsqlConnection writerConnection, long number)
        {
            writerConnection.Execute($@"
                                    insert into requested_blocks (block_no)
                                    values (@number) on conflict do nothing;",
                new
                {
                    number
                });
        }

        public static void InsertEmptyBlock(NpgsqlConnection writerConnection, BlockWithTransactions block)
        {
            var blockTimestamp = block.Timestamp.ToLong();
            var blockTimestampDateTime =
                DateTimeOffset.FromUnixTimeSeconds(blockTimestamp).UtcDateTime;

            writerConnection.Execute($@"
                                    insert into _block_staging (number, hash, timestamp, total_transaction_count)
                                    values (@number, @hash, @timestamp, 0);",
                new
                {
                    number = block.Number.ToLong(),
                    hash = block.BlockHash,
                    timestamp = blockTimestampDateTime
                });
        }
    }
}