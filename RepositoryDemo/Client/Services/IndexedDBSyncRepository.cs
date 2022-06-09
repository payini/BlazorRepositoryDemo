using Microsoft.JSInterop;
using RepositoryDemo.Client;
using System.Reflection;

public class IndexedDBSyncRepository<TEntity> : IRepository<TEntity> where TEntity : class
{
    // injected
    IBlazorDbFactory _dbFactory;
    private readonly APIRepository<TEntity> _apiRepository;
    private readonly IJSRuntime _jsRuntime;
    string _dbName = "";
    string _primaryKeyName = "";
    bool _autoGenerateKey;

    IndexedDbManager manager;
    string storeName = "";
    Type entityType;
    PropertyInfo primaryKey;

    public string LocalStoreName
    {
        get { return $"{storeName}{Globals.LocalTransactionsSuffix}"; }
    }

    public bool IsOnline { get; set; } = true;

    [JSInvokable("ConnectivityChanged")]
    public async void OnConnectivityChanged(bool isOnline)
    {
        if (IsOnline != isOnline)
        {
            IsOnline = isOnline;
        }

        if (IsOnline)
        {
            await SyncLocalToServer();
        }
    }

    public IndexedDBSyncRepository(string dbName, string primaryKeyName, bool autoGenerateKey, IBlazorDbFactory dbFactory, APIRepository<TEntity> apiRepository, IJSRuntime jsRuntime)
    {
        _dbName = dbName;
        _dbFactory = dbFactory;
        _apiRepository = apiRepository;
        _jsRuntime = jsRuntime;
        _primaryKeyName = primaryKeyName;
        _autoGenerateKey = autoGenerateKey;

        entityType = typeof(TEntity);
        storeName = entityType.Name;
        primaryKey = entityType.GetProperty(primaryKeyName);

        _ = _jsRuntime.InvokeVoidAsync("connectivity.initialize", DotNetObjectReference.Create(this));
    }

    private async Task EnsureManager()
    {
        if (manager == null)
        {
            manager = await _dbFactory.GetDbManager(_dbName);
            await manager.OpenDb();
        }
    }
    public async Task DeleteAllAsync()
    {
        if (IsOnline)
            await _apiRepository.DeleteAllAsync();
        else
            await DeleteAllOfflineAsync();
    }

    private async Task DeleteAllOfflineAsync()
    {
        await EnsureManager();
        await manager.ClearTableAsync(storeName);
        RecordDeleteAllAsync();
    }

    public async void RecordDeleteAllAsync()
    {
        var action = LocalTransactionTypes.DeleteAll;
        var record = new StoreRecord<LocalTransaction<TEntity>>()
        {
            StoreName = LocalStoreName,
            Record = new LocalTransaction<TEntity> { Entity = null, Action = action, ActionName = action.ToString() }
        };

        await manager.AddRecordAsync(record);
    }

    public async Task<bool> DeleteAsync(TEntity EntityToDelete)
    {
        if (IsOnline)
            return await _apiRepository.DeleteAsync(EntityToDelete);
        else
            return await DeleteOfflineAsync(EntityToDelete);
    }

    public async Task<bool> DeleteOfflineAsync(TEntity EntityToDelete)
    {
        await EnsureManager();
        var Id = primaryKey.GetValue(EntityToDelete);
        return await DeleteByIdAsync(Id);
    }

    public async Task<bool> DeleteByIdAsync(object Id)
    {
        if (IsOnline)
            return await _apiRepository.DeleteByIdAsync(Id);
        else
            return await DeleteByIdOfflineAsync(Id);
    }

    public async Task<bool> DeleteByIdOfflineAsync(object Id)
    {
        await EnsureManager();
        try
        {
            RecordDeleteByIdAsync(Id);
            await manager.DeleteRecordAsync(storeName, Id);
            return true;
        }
        catch (Exception ex)
        {
            // log exception
            return false;
        }
    }

    public async void RecordDeleteByIdAsync(object id)
    {
        var action = LocalTransactionTypes.DeleteByEntity;

        var entity = await GetByIdAsync(id);

        var record = new StoreRecord<LocalTransaction<TEntity>>()
        {
            StoreName = LocalStoreName,
            Record = new LocalTransaction<TEntity> { Entity = entity, Action = action, ActionName = action.ToString(), Id = int.Parse(id.ToString()) }
        };

        await manager.AddRecordAsync(record);
    }

    public async Task<IEnumerable<TEntity>> GetAllAsync()
    {
        if (IsOnline)
            return await _apiRepository.GetAllAsync();
        else
            return await GetAllOfflineAsync();
    }

    public async Task<IEnumerable<TEntity>> GetAllOfflineAsync()
    {
        await EnsureManager();
        var array = await manager.ToArray<TEntity>(storeName);
        if (array == null)
            return new List<TEntity>();
        else
            return array.ToList();
    }

    public async Task<IEnumerable<TEntity>> GetAsync(QueryFilter<TEntity> Filter)
    {
        // We have to load all items and use LINQ to filter them. :(
        var allitems = await GetAllAsync();
        return Filter.GetFilteredList(allitems);
    }

    public async Task<TEntity> GetByIdAsync(object Id)
    {
        if (IsOnline)
            return await _apiRepository.GetByIdAsync(Id);
        else
            return await GetByIdOfflineAsync(Id);
    }

    public async Task<TEntity> GetByIdOfflineAsync(object Id)
    {
        await EnsureManager();
        var items = await manager.Where<TEntity>(storeName, _primaryKeyName, Id);
        if (items.Any())
            return items.First();
        else
            return null;
    }

    public async Task<TEntity> InsertAsync(TEntity Entity)
    {
        if (IsOnline)
            return await _apiRepository.InsertAsync(Entity);
        else
            return await InsertOfflineAsync(Entity);
    }

    public async Task<TEntity> InsertOfflineAsync(TEntity Entity)
    {
        await EnsureManager();

        // set Id field to zero if the key is autogenerated
        if (_autoGenerateKey)
        {
            primaryKey.SetValue(Entity, 0);
        }

        try
        {
            var record = new StoreRecord<TEntity>()
            {
                StoreName = storeName,
                Record = Entity
            };
            var entity = await manager.AddRecordAsync<TEntity>(record);

            var allItems = await GetAllAsync();
            var last = allItems.Last();

            RecordInsertAsync(last);

            return last;
        }
        catch (Exception ex)
        {
            // log exception
            return null;
        }
    }

    public async void RecordInsertAsync(TEntity Entity)
    {
        try
        {
            var action = LocalTransactionTypes.Insert;

            var record = new StoreRecord<LocalTransaction<TEntity>>()
            {
                StoreName = LocalStoreName,
                Record = new LocalTransaction<TEntity> { Entity = Entity, Action = action, ActionName = action.ToString() }
            };

            await manager.AddRecordAsync(record);
        }
        catch (Exception ex)
        {
            // log exception
        }
    }

    public async Task<TEntity> UpdateAsync(TEntity EntityToUpdate)
    {
        if (IsOnline)
            return await _apiRepository.UpdateAsync(EntityToUpdate);
        else
            return await UpdateOfflineAsync(EntityToUpdate);
    }

    public async Task<TEntity> UpdateOfflineAsync(TEntity EntityToUpdate)
    {
        await EnsureManager();
        object Id = primaryKey.GetValue(EntityToUpdate);
        try
        {
            await manager.UpdateRecord(new UpdateRecord<TEntity>()
            {
                StoreName = storeName,
                Record = EntityToUpdate,
                Key = Id
            });

            RecordUpdateAsync(EntityToUpdate);

            return EntityToUpdate;
        }
        catch (Exception ex)
        {
            // log exception
            return null;
        }
    }

    public async void RecordUpdateAsync(TEntity Entity)
    {
        try
        {
            var action = LocalTransactionTypes.UpdateById;

            var record = new StoreRecord<LocalTransaction<TEntity>>()
            {
                StoreName = LocalStoreName,
                Record = new LocalTransaction<TEntity> { Entity = Entity, Action = action, ActionName = action.ToString() }
            };

            await manager.AddRecordAsync(record);
        }
        catch (Exception ex)
        {
            // log exception
        }
    }
    private async Task DeleteAllTransactionsAsync()
    {
        await EnsureManager();
        await manager.ClearTableAsync(LocalStoreName);
    }

    public async Task<bool> SyncLocalToServer()
    {
        await EnsureManager();

        var array = await manager.ToArray<LocalTransaction<TEntity>>(LocalStoreName);
        if (array == null || array.Count == 0)
            return true;
        else
        {
            foreach (var localTransaction in array.ToList())
            {
                switch (localTransaction.Action)
                {
                    case LocalTransactionTypes.Insert:
                        var insertedEntity = await _apiRepository.InsertAsync(localTransaction.Entity);
                        await UpdateOfflineIds(insertedEntity, localTransaction.Entity);
                        break;

                    case LocalTransactionTypes.UpdateById:
                        await _apiRepository.UpdateAsync(localTransaction.Entity);
                        break;

                    case LocalTransactionTypes.DeleteById:
                        await _apiRepository.DeleteByIdAsync(localTransaction.Id);
                        break;

                    case LocalTransactionTypes.DeleteByEntity:
                        await _apiRepository.DeleteAsync(localTransaction.Entity);
                        break;

                    case LocalTransactionTypes.DeleteAll:
                        await _apiRepository.DeleteAllAsync();
                        break;

                    default:
                        break;
                }
            }

            await DeleteAllTransactionsAsync();
            return true;
        }
    }
   
    public async Task<bool> UpdateOfflineIds(TEntity onlineEntity, TEntity offlineEntity)
    {
        await EnsureManager();

        object Id = primaryKey.GetValue(offlineEntity);

        var array = await manager.ToArray<LocalTransaction<TEntity>>(LocalStoreName);
        if (array == null)
            return false;
        else
        {
            var items = array.Where(i => i.Entity != null).ToList();

            foreach (var item in items)
            {
               var updatedEntity = await UpdateOfflineAsync(item, onlineEntity);
            }
        }

        return true;
    }
    public async Task<LocalTransaction<TEntity>> UpdateOfflineAsync(LocalTransaction<TEntity> entityToUpdate, TEntity onlineEntity)
    {
        await EnsureManager();

        object Id = primaryKey.GetValue(entityToUpdate.Entity);

        entityToUpdate.Entity = onlineEntity;

        try
        {
            await manager.UpdateRecord(new UpdateRecord<LocalTransaction<TEntity>>()
            {
                StoreName = LocalStoreName,
                Record = entityToUpdate,
                Key = Id
            });

            return entityToUpdate;
        }
        catch (Exception ex)
        {
            // log exception
            return null;
        }
    }

    public async ValueTask DisposeAsync()
    {
        await _jsRuntime.InvokeVoidAsync("connectivity.dispose");
    }
}
