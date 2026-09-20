using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Text.Json;

namespace AlekrythaeCore.Tests;

[TestClass]
public sealed class PortableGameStoreApiContractTests
{
    [TestMethod]
    public void TryHandleApi_UnknownOperation_ReturnsUnsupportedOperation()
    {
        var store = new PortableGameStore(System.IO.Path.GetTempPath());

        bool handled = store.TryHandleApi(
            "unknown.operation",
            default,
            out object result);

        Assert.IsFalse(handled);

        JsonElement json = JsonSerializer.SerializeToElement(result);
        Assert.IsFalse(json.GetProperty("ok").GetBoolean());
        Assert.AreEqual(
            "unsupported_portable_db_operation",
            json.GetProperty("error").GetString());
    }
}
