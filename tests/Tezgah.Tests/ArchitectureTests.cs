using System.Reflection;
using Tezgah.Appointments;
using Tezgah.Core.Modules;
using Tezgah.Customers;
using Tezgah.Fleet;
using Tezgah.Inventory;
using Tezgah.Loyalty;
using Tezgah.Orders;

namespace Tezgah.Tests;

public sealed class ArchitectureTests
{
    private static readonly Assembly[] Modules =
    [
        typeof(AppointmentsModule).Assembly,
        typeof(CustomersModule).Assembly,
        typeof(FleetModule).Assembly,
        typeof(InventoryModule).Assembly,
        typeof(LoyaltyModule).Assembly,
        typeof(OrdersModule).Assembly,
    ];

    /// <summary>
    /// What makes "give company A only fleet" possible: a module compiles against the shared kernel
    /// and nothing else, so removing any other module (or the platform's own code) cannot break it.
    /// </summary>
    [Fact]
    public void A_module_references_only_the_shared_kernel()
    {
        foreach (var module in Modules)
        {
            var tezgahReferences = module.GetReferencedAssemblies()
                .Select(reference => reference.Name!)
                .Where(name => name.StartsWith("Tezgah.", StringComparison.Ordinal));

            Assert.Equal(new[] { "Tezgah.Core" }, tezgahReferences);
        }
    }

    [Fact]
    public void A_module_exposes_only_its_registration_class()
    {
        foreach (var module in Modules)
        {
            var exported = module.GetExportedTypes()
                .Where(type => type.Namespace?.EndsWith(".Migrations", StringComparison.Ordinal) != true);

            Assert.True(typeof(IModule).IsAssignableFrom(Assert.Single(exported)));
        }
    }
}
