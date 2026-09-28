using System.Collections.Generic;

namespace GIS.PostGIS
{
    /// <summary>一次批量提交的结果，包括数据库为新增要素生成的主键。</summary>
    public sealed class PostGISCommitResult
    {
        internal PostGISCommitResult(Dictionary<Feature, long> insertedIds,
            int updatedCount, int deletedCount)
        {
            InsertedIds = new Dictionary<Feature, long>(insertedIds);
            UpdatedCount = updatedCount;
            DeletedCount = deletedCount;
        }

        public IDictionary<Feature, long> InsertedIds { get; }
        public int InsertedCount { get { return InsertedIds.Count; } }
        public int UpdatedCount { get; }
        public int DeletedCount { get; }
        public int TotalCount { get { return InsertedCount + UpdatedCount + DeletedCount; } }
    }
}
