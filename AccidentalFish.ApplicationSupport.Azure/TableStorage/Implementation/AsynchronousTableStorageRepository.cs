﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using AccidentalFish.ApplicationSupport.Core.Policies;
using Microsoft.WindowsAzure.Storage;
using Microsoft.WindowsAzure.Storage.RetryPolicies;
using Microsoft.WindowsAzure.Storage.Table;

namespace AccidentalFish.ApplicationSupport.Azure.TableStorage.Implementation
{
    internal class AsynchronousTableStorageRepository<T> : IAsynchronousTableStorageRepository<T> where T : ITableEntity, new()
    {
        private readonly ITableStorageQueryBuilder _tableStorageQueryBuilder;
        private readonly ITableContinuationTokenSerializer _tableContinuationTokenSerializer;
        private readonly CloudTable _table;

        public AsynchronousTableStorageRepository(
            string connectionString,
            string tableName,
            ITableStorageQueryBuilder tableStorageQueryBuilder,
            ITableContinuationTokenSerializer tableContinuationTokenSerializer)
        {
            _tableStorageQueryBuilder = tableStorageQueryBuilder;
            _tableContinuationTokenSerializer = tableContinuationTokenSerializer;

            CloudStorageAccount storageAccount = CloudStorageAccount.Parse(connectionString);
            CloudTableClient tableClient = storageAccount.CreateCloudTableClient();
            tableClient.DefaultRequestOptions.RetryPolicy = new ExponentialRetry(TimeSpan.FromSeconds(120), 3);
            _table = tableClient.GetTableReference(tableName);            
        }

        public async Task InsertAsync(T item)
        {
            TableOperation operation = TableOperation.Insert(item);
            try
            {
                await _table.ExecuteAsync(operation);
            }
            catch (StorageException ex)
            {
                if (ex.RequestInformation.HttpStatusCode == 409)
                {
                    throw new UniqueKeyViolationException(item.PartitionKey, item.RowKey, ex);
                }
                throw;
            }
            
        }

        public Task InsertBatchAsync(IEnumerable<T> items)
        {
            TableBatchOperation operation = new TableBatchOperation();
            foreach (T item in items)
            {
                operation.Insert(item);
            }

            return _table.ExecuteBatchAsync(operation);
        }

        public Task InsertOrReplaceBatchAsync(IEnumerable<T> items)
        {
            TableBatchOperation operation = new TableBatchOperation();
            foreach (T item in items)
            {
                operation.InsertOrReplace(item);
            }
            return _table.ExecuteBatchAsync(operation);
        }

        public Task<T> GetAsync(string partitionKey, string rowKey)
        {
            return Task.Run(() =>
            {
                TableOperation operation = TableOperation.Retrieve<T>(partitionKey, rowKey);
                TableResult result = _table.Execute(operation);
                return (T)result.Result;
            });
        }

        public Task<IEnumerable<T>> GetAsync(string partitionKey)
        {
            return Task.Run(() =>
            {
                TableQuery<T> query =
                    new TableQuery<T>().Where(TableQuery.GenerateFilterCondition("PartitionKey", QueryComparisons.Equal,
                        partitionKey));
                return (IEnumerable<T>)_table.ExecuteQuery(query).ToList();
            });
        }

        public Task<IEnumerable<T>> GetAsync(string partitionKey, int take)
        {
            return Task.Run(() =>
            {
                TableQuery<T> query =
                    new TableQuery<T>().Where(TableQuery.GenerateFilterCondition("PartitionKey", QueryComparisons.Equal,
                        partitionKey)).Take(take);
                return (IEnumerable<T>)_table.ExecuteQuery(query).ToList();
            });
        }

        public Task InsertOrUpdateAsync(T item)
        {
            TableOperation operation = TableOperation.InsertOrMerge(item);
            return _table.ExecuteAsync(operation);
        }

        public Task InsertOrReplaceAsync(T item)
        {
            TableOperation operation = TableOperation.InsertOrReplace(item);
            return _table.ExecuteAsync(operation);
        }

        public async Task<bool> DeleteAsync(T item)
        {
            TableOperation operation = TableOperation.Delete(item);
            TableResult result = await _table.ExecuteAsync(operation);
            return result.HttpStatusCode == 204;
        }

        public Task UpdateAsync(T item)
        {
            TableOperation operation = TableOperation.Replace(item);
            return _table.ExecuteAsync(operation);
        }

        #region Querying

        public async Task<IEnumerable<T>> QueryAsync(string column, string value)
        {
            List<T> results = new List<T>();
            TableQuery<T> query = new TableQuery<T>().Where(TableQuery.GenerateFilterCondition(column, QueryComparisons.Equal, value));
            TableQuerySegment<T> querySegment = null;

            while (querySegment == null || querySegment.ContinuationToken != null)
            {
                querySegment = await _table.ExecuteQuerySegmentedAsync(query, querySegment != null ? querySegment.ContinuationToken : null);
                results.AddRange(querySegment.Results);
            }
            return results;
        }

        public async Task<IEnumerable<T>> QueryAsync(Dictionary<string, object> columnValues)
        {   
            List<T> results = new List<T>();

            TableQuery<T> query = _tableStorageQueryBuilder.TableQuery<T>(columnValues, TableStorageQueryOperator.And);
            TableQuerySegment<T> querySegment = null;

            while (querySegment == null || querySegment.ContinuationToken != null)
            {
                querySegment = await _table.ExecuteQuerySegmentedAsync(query, querySegment != null ? querySegment.ContinuationToken : null);
                results.AddRange(querySegment.Results);
            }
            return results;
        }

        public async Task QueryActionAsync(string column, string value, Func<IEnumerable<T>, Task> action)
        {
            TableQuery<T> query;
            if (!String.IsNullOrWhiteSpace(column))
            {
                query = new TableQuery<T>().Where(TableQuery.GenerateFilterCondition(column, QueryComparisons.Equal, value));
            }
            else
            {
                query = new TableQuery<T>();
            }
            
            TableQuerySegment<T> querySegment = null;

            while (querySegment == null || querySegment.ContinuationToken != null)
            {
                querySegment = await _table.ExecuteQuerySegmentedAsync(query, querySegment != null ? querySegment.ContinuationToken : null);
                await action(new List<T>(querySegment.Results));
            }
        }

        public async Task QueryFuncAsync(string column, string value, Func<IEnumerable<T>, Task<bool>> func)
        {
            TableQuery<T> query;
            if (!String.IsNullOrWhiteSpace(column))
            {
                query = new TableQuery<T>().Where(TableQuery.GenerateFilterCondition(column, QueryComparisons.Equal, value));
            }
            else
            {
                query = new TableQuery<T>();
            }

            TableQuerySegment<T> querySegment = null;
            bool doContinue = true;

            while ((querySegment == null || querySegment.ContinuationToken != null) && doContinue)
            {
                querySegment = await _table.ExecuteQuerySegmentedAsync(query, querySegment != null ? querySegment.ContinuationToken : null);
                doContinue = await func(new List<T>(querySegment.Results));
            }
        }

        public async Task QueryFuncAsync(string column, IEnumerable<object> values, TableStorageQueryOperator op,
            Func<IEnumerable<T>, Task<bool>> func)
        {
            TableQuery<T> query;
            if (!String.IsNullOrWhiteSpace(column) && values != null && values.Any())
            {
                query = _tableStorageQueryBuilder.TableQuery<T>(column, values, op);
            }
            else
            {
                query = new TableQuery<T>();
            }

            TableQuerySegment<T> querySegment = null;
            bool doContinue = true;

            while ((querySegment == null || querySegment.ContinuationToken != null) && doContinue)
            {
                querySegment = await _table.ExecuteQuerySegmentedAsync(query, querySegment != null ? querySegment.ContinuationToken : null);
                doContinue = await func(new List<T>(querySegment.Results));
            }
        }

        public async Task QueryFuncAsync(Dictionary<string, object> conditions, TableStorageQueryOperator op, Func<IEnumerable<T>, Task<bool>> func)
        {
            TableQuery<T> query;
            if (conditions != null)
            {
                query = _tableStorageQueryBuilder.TableQuery<T>(conditions, op);
            }
            else
            {
                query = new TableQuery<T>();
            }

            TableQuerySegment<T> querySegment = null;
            bool doContinue = true;

            while ((querySegment == null || querySegment.ContinuationToken != null) && doContinue)
            {
                querySegment = await _table.ExecuteQuerySegmentedAsync(query, querySegment != null ? querySegment.ContinuationToken : null);
                doContinue = await func(new List<T>(querySegment.Results));
            }
        }

        public async Task AllActionAsync(Func<IEnumerable<T>, Task> action)
        {
            await QueryActionAsync(null, null, action);
        }

        public Task<PagedResultSegment<T>> PagedQueryAsync(Dictionary<string, object> columnValues, int pageSize)
        {
            return PagedQueryAsync(columnValues, TableStorageQueryOperator.And, pageSize);
        }

        public Task<PagedResultSegment<T>> PagedQueryAsync(Dictionary<string, object> columnValues, int pageSize, string serializedContinuationToken)
        {
            return PagedQueryAsync(columnValues, TableStorageQueryOperator.And, pageSize, serializedContinuationToken);
        }

        #endregion

        #region Paged queries

        public Task<PagedResultSegment<T>> PagedQueryAsync(Dictionary<string, object> columnValues, TableStorageQueryOperator op, int pageSize)
        {
            return PagedQueryAsync(columnValues, pageSize, null);
        }

        public async Task<PagedResultSegment<T>> PagedQueryAsync(Dictionary<string, object> columnValues, TableStorageQueryOperator op, int pageSize, string serializedContinuationToken)
        {
            TableQuery<T> query = _tableStorageQueryBuilder.TableQuery<T>(columnValues, op).Take(pageSize);

            TableQuerySegment<T> querySegment = null;
            TableContinuationToken continuationToken = _tableContinuationTokenSerializer.Deserialize(serializedContinuationToken);

            querySegment = await _table.ExecuteQuerySegmentedAsync(query, continuationToken);

            return new PagedResultSegment<T>
            {
                ContinuationToken = _tableContinuationTokenSerializer.Serialize(querySegment.ContinuationToken),
                Page = new List<T>(querySegment.Results)
            };
        }

        public async Task<PagedResultSegment<T>> PagedQueryAsync(string filter, int pageSize,
            string serializedContinuationToken)
        {
            TableQuery<T> tableQuery = new TableQuery<T>();
            tableQuery.Where(filter);

            TableQuerySegment<T> querySegment = null;
            TableContinuationToken continuationToken = _tableContinuationTokenSerializer.Deserialize(serializedContinuationToken);

            querySegment = await _table.ExecuteQuerySegmentedAsync(tableQuery, continuationToken);

            return new PagedResultSegment<T>
            {
                ContinuationToken = _tableContinuationTokenSerializer.Serialize(querySegment.ContinuationToken),
                Page = new List<T>(querySegment.Results)
            };
        }

        #endregion

        public IResourceCreator GetResourceCreator()
        {
            return new AzureTableCreator(_table);
        }
    }
}
