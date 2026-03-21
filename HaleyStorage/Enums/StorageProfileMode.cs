namespace Haley.Enums {
    public enum StorageProfileMode {
        DirectSave = 0,              // New uploads go straight to PrimaryProvider
        StageAndMove = 1,     // New uploads go to SecondaryProvider, background worker moves to Primary
        StageAndRetainCopy = 2   // New uploads go to Secondary, worker copies to Primary but keeps staging copy
    }
}