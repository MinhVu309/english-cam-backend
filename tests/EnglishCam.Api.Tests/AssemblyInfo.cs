using Xunit;

// Các factory đặt cấu hình qua biến môi trường của cả process nên chạy tuần tự.
[assembly: CollectionBehavior(DisableTestParallelization = true)]
