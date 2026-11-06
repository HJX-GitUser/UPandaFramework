mergeInto(LibraryManager.library, {
  $UPandaStorage__deps: ['$FS'],
  $UPandaStorage: {
    pending: [], running: false,
    flush: function() {
      if (UPandaStorage.running || !UPandaStorage.pending.length) return;
      var batch = UPandaStorage.pending.splice(0); UPandaStorage.running = true;
      var done = function(error) {
        UPandaStorage.running = false;
        for (var i=0; i<batch.length; i++) {
          var callback = batch[i][1];
          {{{ makeDynCall('vii', 'callback') }}}(batch[i][0], error ? 1 : 0);
        }
        UPandaStorage.flush();
      };
      try { FS.syncfs(false, done); } catch (error) { done(error); }
    }
  },
  UPanda_SyncPersistent__deps: ['$UPandaStorage'],
  UPanda_SyncPersistent: function(id, callback) {
    UPandaStorage.pending.push([id, callback]); UPandaStorage.flush();
  },
  UPanda_StorageQuota: function(id, callback) {
    var done = function(bytes) { {{{ makeDynCall('vid', 'callback') }}}(id, bytes); };
    if (typeof navigator === 'undefined' || !navigator.storage || !navigator.storage.estimate) { done(-1); return; }
    navigator.storage.estimate().then(function(value) {
      done(typeof value.quota === 'number' && typeof value.usage === 'number' ? Math.max(0,value.quota-value.usage) : -1);
    }).catch(function() { done(-1); });
  }
});
