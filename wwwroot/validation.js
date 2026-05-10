/**
 * Taskr — Shared Client-Side Validation Library
 */

// ── Core validator ────────────────────────────────────────────────────────────
const Validator = {
  rules: {
    required:    (v)        => v.trim() !== ''                              || 'This field is required.',
    minLen:      (n) => (v) => v.trim().length >= n                         || `Must be at least ${n} characters.`,
    maxLen:      (n) => (v) => v.trim().length <= n                         || `Must be ${n} characters or fewer.`,
    email:       (v)        => /^[^\s@]+@[^\s@]+\.[^\s@]+$/.test(v.trim()) || 'Enter a valid email address.',
    username:    (v)        => /^[a-zA-Z0-9_]+$/.test(v.trim())            || 'Only letters, numbers, and underscores allowed.',
    minNum:      (n) => (v) => !v || Number(v) >= n                         || `Minimum value is ${n}.`,
    maxNum:      (n) => (v) => !v || Number(v) <= n                         || `Maximum value is ${n}.`,
    noFutureDate:(v)        => !v || new Date(v) >= new Date(new Date().toDateString()) || 'Due date cannot be in the past.',
    priority:    (v)        => ['low','medium','high'].includes(v)          || 'Select a valid priority.',
  },

  /** Run rules against a value, returns first error string or null */
  run(value, ...ruleFns) {
    for (const fn of ruleFns) {
      const result = fn(value);
      if (result !== true) return result;
    }
    return null;
  }
};

// ── Field error UI helpers ────────────────────────────────────────────────────
function setFieldError(fieldEl, message) {
  fieldEl.classList.add('is-invalid');
  fieldEl.classList.remove('is-valid');
  let fb = fieldEl.parentElement.querySelector('.invalid-feedback');
  if (!fb) {
    fb = document.createElement('div');
    fb.className = 'invalid-feedback';
    fieldEl.parentElement.appendChild(fb);
  }
  fb.textContent = message;
}

function setFieldValid(fieldEl) {
  fieldEl.classList.remove('is-invalid');
  fieldEl.classList.add('is-valid');
  const fb = fieldEl.parentElement.querySelector('.invalid-feedback');
  if (fb) fb.textContent = '';
}

function clearFieldState(fieldEl) {
  fieldEl.classList.remove('is-invalid', 'is-valid');
}

/** Validate a single field; returns true if valid */
function validateField(fieldEl, ...ruleFns) {
  const err = Validator.run(fieldEl.value, ...ruleFns);
  if (err) { setFieldError(fieldEl, err); return false; }
  setFieldValid(fieldEl);
  return true;
}

/** Attach live validation to a field on blur + input */
function attachLiveValidation(fieldEl, ...ruleFns) {
  const check = () => {
    if (fieldEl.value !== '') validateField(fieldEl, ...ruleFns);
  };
  fieldEl.addEventListener('blur', () => validateField(fieldEl, ...ruleFns));
  fieldEl.addEventListener('input', check);
}

// ── Server error handler ──────────────────────────────────────────────────────
/**
 * Map server validation error response onto form fields.
 * errorsObj: { fieldName: "message", ... }
 * fieldMap:  { fieldName: HTMLElement, ... }
 */
function applyServerErrors(errorsObj, fieldMap, generalEl) {
  for (const [key, msg] of Object.entries(errorsObj || {})) {
    if (key === 'general' && generalEl) {
      showFormAlert(generalEl, 'danger', msg);
    } else if (fieldMap[key]) {
      setFieldError(fieldMap[key], msg);
    }
  }
}

// ── Form alert banner ─────────────────────────────────────────────────────────
function showFormAlert(el, type, msg) {
  const icons = { danger: 'bi-exclamation-circle', success: 'bi-check-circle', warning: 'bi-exclamation-triangle' };
  el.className = `alert alert-${type} rounded-3`;
  el.innerHTML = `<i class="bi ${icons[type] || ''} me-2"></i>${msg}`;
}
function hideFormAlert(el) { el.className = 'alert d-none'; }

// ── Loading state ─────────────────────────────────────────────────────────────
function setLoading(btnEl, on) {
  const lbl = btnEl.querySelector('.btn-label');
  const spn = btnEl.querySelector('.btn-spin');
  if (lbl) lbl.classList.toggle('d-none', on);
  if (spn) spn.classList.toggle('d-none', !on);
  btnEl.disabled = on;
}
