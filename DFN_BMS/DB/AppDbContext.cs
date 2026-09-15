using DFN_BMS.Models;
using Microsoft.EntityFrameworkCore;
using System.Collections.Generic;

namespace DFN_BMS.DB
{
    public class AppDbContext : DbContext
    {
        public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }
        public DbSet<UserMaster> UserMasters { get; set; }
        public DbSet<DepartmentMaster> DepartmentMasters { get; set; }
        public DbSet<UserPrivilege> UserPrivileges { get; set; }
        public DbSet<Report> Reports { get; set; }
        public DbSet<ErrorLog> ErrorLogs { get; set; }
        public DbSet<MailSettings> MAIL_SETTINGS { get; set; }
        public DbSet<ItemGroupMaster> ItemGroupMasters { get; set; }
        public DbSet<ItemMaster> ItemMasters { get; set; }
        public DbSet<ItemTypeMaster> ItemTypeMasters { get; set; }
        public DbSet<SupplierGroupMaster> SupplierGroupMasters { get; set; }
        public DbSet<SupplierMaster> SupplierMasters { get; set; }
        public DbSet<CustomerGroupMaster> CustomerGroupMasters { get; set; }
        public DbSet<CustomerMaster> CustomerMasters { get; set; }
        public DbSet<PriceMaster> PriceMasters { get; set; }
        public DbSet<StoreMaster> StoreMasters { get; set; }
        public DbSet<LocationMaster> LocationMasters { get; set; }
        public DbSet<LocationRack> LocationRacks { get; set; }
        public DbSet<RackColumn> RackColumns { get; set; }
        public DbSet<RackRow> RackRows { get; set; }
        public DbSet<GrnHeader> GrnHeaders { get; set; }
        public DbSet<GrnLine> GrnLines { get; set; }
        public DbSet<GrnPallet> GrnPallets { get; set; }
        public DbSet<StorePosition> StorePositions { get; set; }
        public DbSet<StoreMovement> StoreMovements { get; set; }
        public DbSet<PalletTypeMaster> PalletTypeMasters { get; set; }
        public DbSet<GrnCounter> GrnCounters { get; set; }
        public DbSet<MaterialIssue> MaterialIssues { get; set; }
        public DbSet<UomMaster> UomMasters { get; set; }
        public DbSet<StoreVerification> StoreVerification { get; set; }

        public DbSet<PalletIssueLock> PalletIssueLocks { get; set; }
    }
}
