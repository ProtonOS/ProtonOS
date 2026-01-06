// ProtonOS - Test Driver
// A simple test driver to verify dynamic driver loading from /drivers.
// This driver does nothing except log messages to verify the loading mechanism.

using ProtonOS.DDK.Kernel;

namespace ProtonOS.Drivers.Test;

/// <summary>
/// Test driver entry point.
/// This class demonstrates the driver entry pattern used by DriverLoader.
/// </summary>
public static class TestDriverEntry
{
    private static bool _initialized;
    private static int _initCount;

    /// <summary>
    /// Called by DriverLoader when the driver is loaded.
    /// Returns true if initialization succeeded.
    /// </summary>
    public static bool Initialize()
    {
        Debug.WriteLine("[TestDriver] Initialize called!");

        if (_initialized)
        {
            Debug.WriteLine("[TestDriver] Already initialized");
            return true;
        }

        _initCount++;
        Debug.Write("[TestDriver] Init count: ");
        Debug.WriteDecimal(_initCount);
        Debug.WriteLine();

        // Perform any driver-specific initialization here
        // For this test driver, we just log success

        Debug.WriteLine("[TestDriver] Running self-tests...");

        // Test 1: Basic functionality
        if (!TestBasicFunctionality())
        {
            Debug.WriteLine("[TestDriver] FAIL: Basic functionality test");
            return false;
        }
        Debug.WriteLine("[TestDriver] PASS: Basic functionality");

        // Test 2: DDK access
        if (!TestDDKAccess())
        {
            Debug.WriteLine("[TestDriver] FAIL: DDK access test");
            return false;
        }
        Debug.WriteLine("[TestDriver] PASS: DDK access");

        _initialized = true;
        Debug.WriteLine("[TestDriver] Initialization complete!");

        return true;
    }

    /// <summary>
    /// Test basic driver functionality.
    /// </summary>
    private static bool TestBasicFunctionality()
    {
        // Test basic arithmetic (verifies JIT works)
        int a = 42;
        int b = 58;
        int sum = a + b;

        if (sum != 100)
        {
            Debug.Write("[TestDriver] Expected 100, got ");
            Debug.WriteDecimal(sum);
            Debug.WriteLine();
            return false;
        }

        // Test string operations
        string testStr = "Hello from TestDriver!";
        if (testStr.Length != 22)
        {
            Debug.Write("[TestDriver] String length mismatch: ");
            Debug.WriteDecimal(testStr.Length);
            Debug.WriteLine();
            return false;
        }

        return true;
    }

    /// <summary>
    /// Test DDK access from a dynamically loaded driver.
    /// </summary>
    private static bool TestDDKAccess()
    {
        // Test Debug output (verifies DDK PInvoke works)
        Debug.Write("[TestDriver] DDK Debug.Write works: 0x");
        Debug.WriteHex(0xDEADBEEFul);
        Debug.WriteLine();

        // Simple test - just verify we can call DDK functions
        Debug.WriteLine("[TestDriver] DDK access verified");

        return true;
    }

    /// <summary>
    /// Called when the driver is being unloaded.
    /// </summary>
    public static void Shutdown()
    {
        Debug.WriteLine("[TestDriver] Shutdown called");
        _initialized = false;
    }

    /// <summary>
    /// Get the driver version string.
    /// </summary>
    public static string GetVersion()
    {
        return "1.0.0-test";
    }

    /// <summary>
    /// Check if the driver is initialized.
    /// </summary>
    public static bool IsInitialized => _initialized;
}
