namespace ForgeMap.Benchmarks.Models;

// Source types (4-level deep: Company -> Department -> Team -> Employee)

public class CompanySource
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public DepartmentSource Department { get; set; } = null!;
}

public class DepartmentSource
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Code { get; set; } = string.Empty;
    public TeamSource Team { get; set; } = null!;
}

public class TeamSource
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public EmployeeSource Lead { get; set; } = null!;
}

public class EmployeeSource
{
    public int Id { get; set; }
    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
}

// Destination types

public class CompanyDestination
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public DepartmentDestination Department { get; set; } = null!;
}

public class DepartmentDestination
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Code { get; set; } = string.Empty;
    public TeamDestination Team { get; set; } = null!;
}

public class TeamDestination
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public EmployeeDestination Lead { get; set; } = null!;
}

public class EmployeeDestination
{
    public int Id { get; set; }
    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
}
