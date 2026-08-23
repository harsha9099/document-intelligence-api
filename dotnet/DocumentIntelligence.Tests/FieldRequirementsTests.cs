using DocumentIntelligence.Api.Services;

namespace DocumentIntelligence.Tests;

public class FieldRequirementsTests
{
    [Fact]
    public void Check_AllFieldsPresent_ReturnsComplete()
    {
        var content = new Dictionary<string, object>
        {
            ["id_number"] = "123456",
            ["full_name"] = "John Doe",
            ["date_of_birth"] = "1990-01-01"
        };

        var (isComplete, missing) = FieldRequirements.Check("identity_document", content);

        Assert.True(isComplete);
        Assert.Empty(missing);
    }

    [Fact]
    public void Check_MissingField_ReturnsIncomplete()
    {
        var content = new Dictionary<string, object>
        {
            ["id_number"] = "123456",
            ["full_name"] = "John Doe"
        };

        var (isComplete, missing) = FieldRequirements.Check("identity_document", content);

        Assert.False(isComplete);
        Assert.Contains("date_of_birth", missing);
    }

    [Fact]
    public void Check_NullFieldValue_CountsAsMissing()
    {
        var content = new Dictionary<string, object>
        {
            ["id_number"] = "123456",
            ["full_name"] = "John Doe",
            ["date_of_birth"] = null!
        };

        var (isComplete, missing) = FieldRequirements.Check("identity_document", content);

        Assert.False(isComplete);
        Assert.Contains("date_of_birth", missing);
    }

    [Fact]
    public void Check_EmptyStringValue_CountsAsMissing()
    {
        var content = new Dictionary<string, object>
        {
            ["id_number"] = "",
            ["full_name"] = "John Doe",
            ["date_of_birth"] = "1990-01-01"
        };

        var (isComplete, missing) = FieldRequirements.Check("identity_document", content);

        Assert.False(isComplete);
        Assert.Contains("id_number", missing);
    }

    [Fact]
    public void Check_UnknownDocumentType_ReturnsComplete()
    {
        var content = new Dictionary<string, object>();

        var (isComplete, missing) = FieldRequirements.Check("unknown_type", content);

        Assert.True(isComplete);
        Assert.Empty(missing);
    }

    [Fact]
    public void Check_InvoiceAllFieldsPresent_ReturnsComplete()
    {
        var content = new Dictionary<string, object>
        {
            ["invoice_number"] = "INV-001",
            ["total_amount"] = 1500.00,
            ["vendor_name"] = "Acme Corp"
        };

        var (isComplete, missing) = FieldRequirements.Check("invoice", content);

        Assert.True(isComplete);
        Assert.Empty(missing);
    }

    [Fact]
    public void Check_BankStatementMissingTransactions_ReturnsIncomplete()
    {
        var content = new Dictionary<string, object>
        {
            ["account_number"] = "1234567890",
            ["bank_name"] = "FNB"
        };

        var (isComplete, missing) = FieldRequirements.Check("bank_statement", content);

        Assert.False(isComplete);
        Assert.Contains("transactions", missing);
    }

    [Fact]
    public void Check_BillAllFields_ReturnsComplete()
    {
        var content = new Dictionary<string, object>
        {
            ["total_due"] = 350.00,
            ["provider_name"] = "Telkom"
        };

        var (isComplete, missing) = FieldRequirements.Check("bill", content);

        Assert.True(isComplete);
        Assert.Empty(missing);
    }

    [Fact]
    public void Check_PayslipPartialFields_ReturnsMissingList()
    {
        var content = new Dictionary<string, object>
        {
            ["employee_name"] = "Jane Smith"
        };

        var (isComplete, missing) = FieldRequirements.Check("payslip", content);

        Assert.False(isComplete);
        Assert.Contains("gross_pay", missing);
        Assert.Contains("net_pay", missing);
        Assert.Equal(2, missing.Count);
    }
}
