// The API is language-neutral: it returns stable codes, never UI strings (FR-OPS-001, FR-OPS-006).
// The SPA maps every code to an Arabic message here. Unknown codes fall back to a generic message.

export const errorMessages: Record<string, string> = {
  'auth.invalid_credentials': 'اسم المستخدم أو كلمة المرور غير صحيحة.',
  unauthenticated: 'انتهت الجلسة. يرجى تسجيل الدخول مرة أخرى.',
  forbidden: 'ليست لديك صلاحية لتنفيذ هذا الإجراء.',
  not_found: 'العنصر المطلوب غير موجود.',
  validation_failed: 'يرجى مراجعة البيانات المدخلة.',
  malformed_request: 'البيانات المرسلة غير صالحة.',
  conflict: 'تعارض مع بيانات موجودة.',
  rule_violation: 'لا يمكن تنفيذ هذا الإجراء وفق قواعد النظام.',
  'concurrency.stale': 'تم تعديل هذا السجل من مستخدم آخر. يرجى إعادة التحميل ثم المحاولة.',
  'concurrency.precondition_required': 'يرجى إعادة تحميل السجل قبل التعديل.',
  csrf_invalid: 'انتهت صلاحية النموذج. يرجى إعادة تحميل الصفحة.',
  rate_limited: 'محاولات كثيرة. يرجى الانتظار قليلاً ثم المحاولة.',
  method_not_allowed: 'هذا الإجراء غير مدعوم.',
  internal_error: 'حدث خطأ غير متوقع. يرجى المحاولة لاحقاً.',
};

export const fieldMessages: Record<string, string> = {
  required: 'هذا الحقل مطلوب.',
  too_short: 'القيمة أقصر من الحد الأدنى المسموح.',
  too_long: 'القيمة أطول من الحد الأقصى المسموح.',
  out_of_range: 'القيمة خارج النطاق المسموح.',
  invalid: 'القيمة غير صالحة.',
  duplicate: 'هذه القيمة مستخدمة بالفعل.',
  not_allowed: 'هذه القيمة غير مسموح بها.',
  password_denied: 'كلمة المرور شائعة جداً. يرجى اختيار كلمة مرور أخرى.',
  role_mismatch: 'بيانات الموظف لا تطابق الدور المختار.',
  already_converted: 'تم تحويل هذا العميل المحتمل إلى مريض مسبقاً.',
};

export function messageFor(code: string | undefined): string {
  return (code && errorMessages[code]) || errorMessages.internal_error!;
}

export function fieldMessageFor(code: string): string {
  return fieldMessages[code] ?? fieldMessages.invalid!;
}
