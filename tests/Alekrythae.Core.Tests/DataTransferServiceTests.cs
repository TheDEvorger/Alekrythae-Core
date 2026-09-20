using System.IO;
using System.Text.Json;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AlekrythaeCore.Tests;

[TestClass]
public sealed class DataTransferServiceTests
{
    [TestMethod]
    public void TryHandleApi_UnknownOperation_ReturnsUnsupportedOperation()
    {
        var service = new DataTransferService(
            Path.GetTempPath(),
            null!);

        bool handled = service.TryHandleApi(
            "unknown.operation",
            default,
            out object result);

        Assert.IsFalse(handled);

        JsonElement json = JsonSerializer.SerializeToElement(result);

        Assert.IsFalse(json.GetProperty("ok").GetBoolean());
        Assert.AreEqual(
            "unsupported_data_transfer_operation",
            json.GetProperty("error").GetString());
    }
}
