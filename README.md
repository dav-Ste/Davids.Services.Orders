# Davids.Services.Orders

Clean Architecture `.NET 10` checkout and order lifecycle microservice for the Davids platform, backed by SQL Server (`ordersdb`), live catalog verification, and RabbitMQ payment/completion saga orchestration (`orders.order_placed` -> `payments.payment_captured` -> `orders.order_completed`).
