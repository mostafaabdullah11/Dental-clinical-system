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
  // Task 3 — المواعيد والزيارات والوصفات والتحويلات والتقييمات
  'availability.overlap': 'فترات الإتاحة متداخلة لنفس الطبيب.',
  'appointment.invalid_transition': 'لا يمكن تغيير حالة الموعد بهذا الشكل.',
  'appointment.already_rescheduled': 'تمت إعادة جدولة هذا الموعد مسبقاً.',
  'arrival.not_eligible': 'لا يمكن تسجيل الحضور إلا لموعد مفتوح في تاريخ اليوم.',
  'arrival.already_recorded': 'تم تسجيل حضور المريض مسبقاً.',
  'arrival.none': 'لا يوجد حضور مسجل لإلغائه.',
  'visit.already_started': 'تم بدء زيارة لهذا الموعد مسبقاً.',
  'visit.finalized': 'تم إنهاء الزيارة ولا يمكن تعديلها.',
  'catalog.option_invalid': 'اختيارات الإجراء غير صحيحة لهذا النوع من العلاج.',
  'case.closed': 'الحالة العلاجية مغلقة.',
  'treatment.already_recorded': 'هذه الحالة مسجلة بالفعل في هذه الزيارة.',
  'treatment.record_required': 'يجب تسجيل علاج في هذه الزيارة أولاً.',
  'treatment.case_details_locked': 'لا يمكن تعديل تفاصيل حالة بدأت في زيارة أخرى.',
  'treatment.not_implant_case': 'هذه الحالة ليست زراعة.',
  'treatment.not_root_canal_case': 'هذه الحالة ليست علاج عصب.',
  'step.already_completed': 'تم إكمال هذه الخطوة مسبقاً.',
  'workitem.charge_exists': 'تم تثبيت المستحق ولا يمكن تعديل السعر.',
  'pricing.below_discount': 'السعر أقل من الخصم الحالي.',
  'pricing.price_required': 'يجب إدخال السعر أولاً.',
  'discount.exceeds_price': 'الخصم أكبر من السعر.',
  'discount.stale_entry': 'تم تعديل الخصم من مستخدم آخر. يرجى إعادة التحميل.',
  'ortho.single_price': 'حالة التقويم لها سعر واحد فقط.',
  'ortho.use_ortho_route': 'يتم تسجيل التقويم من نموذج التقويم.',
  'ortho.deposit_exceeds_effective_total': 'المقدم أكبر من إجمالي التقويم بعد الخصم.',
  'ortho.total_decrease_not_supported': 'تخفيض إجمالي التقويم غير مدعوم في هذا الإصدار.',
  'fixed_lab.initial_required': 'يجب إدخال تكلفة المعمل الثابتة قبل إنهاء الزيارة.',
  'fixed_lab.already_exists': 'تم إدخال تكلفة المعمل لهذه الحالة مسبقاً.',
  'fixed_lab.not_fixed_model': 'هذه الحالة لا تستخدم نموذج المعمل الثابت.',
  'correction.invalid': 'طلب التصحيح غير صالح.',
  'feedback.already_submitted': 'تم استلام التقييم لهذه الزيارة مسبقاً.',
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
