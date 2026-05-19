using System;
using System.Collections.Generic;

namespace MeezanPOS.Application.Services
{
    public class PostingBatchResult
    {
        public bool Success { get; set; }
        public Guid SessionGuid { get; set; }
        public int PostedCount { get; set; }
        public decimal TotalSales { get; set; }
        public decimal TotalExpenses { get; set; }
        public TimeSpan Duration { get; set; }
        public List<string> Errors { get; set; } = new();
        public Guid CorrelationId { get; set; }
    }
}
