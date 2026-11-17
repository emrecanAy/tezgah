using Tezgah.Appointments;
using Tezgah.Core.Modules;
using Tezgah.Customers;
using Tezgah.Fleet;
using Tezgah.Inventory;
using Tezgah.Loyalty;

namespace Tezgah.Tests;

public sealed class ModuleCatalogTests
{
    private static readonly IModule[] Available =
        [new AppointmentsModule(), new CustomersModule(), new FleetModule(), new InventoryModule(), new LoyaltyModule()];

    [Fact]
    public void An_installation_can_ship_a_single_module()
    {
        var catalog = ModuleCatalog.Create(Available, ["fleet"]);

        Assert.Equal(new[] { "fleet" }, catalog.Modules.Select(module => module.Info.Key));
    }

    [Fact]
    public void Dependencies_are_loaded_before_the_modules_that_need_them()
    {
        var keys = ModuleCatalog.Create(Available, shipped: null).Modules.Select(module => module.Info.Key).ToList();

        Assert.Equal(5, keys.Count);
        Assert.True(keys.IndexOf("customers") < keys.IndexOf("appointments"));
        Assert.True(keys.IndexOf("customers") < keys.IndexOf("loyalty"));
    }

    [Fact]
    public void An_installation_that_ships_a_module_without_its_dependency_does_not_start()
    {
        var error = Assert.Throws<InvalidOperationException>(() => ModuleCatalog.Create(Available, ["loyalty"]));

        Assert.Contains("customers", error.Message);
    }

    [Fact]
    public void A_misspelled_module_in_configuration_does_not_start_either()
    {
        Assert.Throws<InvalidOperationException>(() => ModuleCatalog.Create(Available, ["fleet", "flee"]));
    }
}
