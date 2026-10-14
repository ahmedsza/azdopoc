using System.Data;
using Microsoft.Data.SqlClient;
using PropertyManagement.Web.ViewModels;

namespace PropertyManagement.Web.Data;

public sealed record PaymentRequest(int LeaseId, DateOnly RentalMonth, DateOnly PaidOn, decimal Amount, string Reference, Guid SubmissionKey);

public sealed class RentPaymentService(SqlConnectionFactory connections)
{
    public async Task<int> RecordAsync(PaymentRequest payment, CancellationToken cancellationToken = default)
    {
        await using var connection = connections.Create();
        await connection.OpenAsync(cancellationToken);
        await using var command = new SqlCommand("dbo.usp_RecordRentPayment", connection) { CommandType = CommandType.StoredProcedure };
        command.Parameters.Add("@LeaseId", SqlDbType.Int).Value = payment.LeaseId;
        command.Parameters.Add("@RentalMonth", SqlDbType.Date).Value = payment.RentalMonth.ToDateTime(TimeOnly.MinValue);
        command.Parameters.Add("@PaidOn", SqlDbType.Date).Value = payment.PaidOn.ToDateTime(TimeOnly.MinValue);
        var amount = command.Parameters.Add("@Amount", SqlDbType.Decimal);
        amount.Precision = 18;
        amount.Scale = 2;
        amount.Value = payment.Amount;
        command.Parameters.Add("@Reference", SqlDbType.NVarChar, 100).Value = payment.Reference;
        command.Parameters.Add("@SubmissionKey", SqlDbType.UniqueIdentifier).Value = payment.SubmissionKey;
        return Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken));
    }

    public async Task<IReadOnlyList<LedgerRow>> GetLedgerAsync(DateOnly month, CancellationToken cancellationToken = default)
    {
        await using var connection = connections.Create();
        await connection.OpenAsync(cancellationToken);
        await using var command = new SqlCommand("dbo.usp_GetRentLedger", connection) { CommandType = CommandType.StoredProcedure };
        command.Parameters.Add("@RentalMonth", SqlDbType.Date).Value = month.ToDateTime(TimeOnly.MinValue);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        List<LedgerRow> rows = [];
        while (await reader.ReadAsync(cancellationToken))
            rows.Add(new(reader.GetInt32(0), reader.GetString(1), reader.GetString(2), reader.GetDecimal(3), reader.GetDecimal(4), reader.GetDecimal(5)));
        return rows;
    }
}