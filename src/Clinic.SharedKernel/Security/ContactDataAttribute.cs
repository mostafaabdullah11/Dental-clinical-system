namespace Clinic.SharedKernel.Security;

/// <summary>
/// Marks a type or property that carries C-Contact data (phone, WhatsApp, email, address, CRM notes,
/// Original Sales attribution, patient source). Architecture tests forbid these types under any Doctor
/// feature, and the logging policy never writes them (SECURITY_DESIGN.md §7 D4, §11).
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct | AttributeTargets.Property | AttributeTargets.Interface)]
public sealed class ContactDataAttribute : Attribute;

/// <summary>
/// Marks a type that carries structured Medical History values. They are never written to audit, logs,
/// notifications or exports (FR-MED-006).
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct | AttributeTargets.Property)]
public sealed class MedicalDataAttribute : Attribute;
