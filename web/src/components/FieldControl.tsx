import { useEffect, useState } from 'react';
import { api, type ReferenceItem } from '../api/client';
import { evaluateAsBool, type EvalContext } from '../engine/evaluate';
import { localized } from '../engine/texts';
import type { Json, TemplateField, ValidationMessage } from '../engine/types';
import { MessageList } from './Messages';

interface Props {
  field: TemplateField;
  value: Json | undefined;
  ctx: EvalContext;
  messages: ValidationMessage[];
  culture: string;
  onChange: (value: Json) => void;
  onBlur?: () => void;
}

/**
 * Renders one attribute from its template description. Nothing here is figure-specific: the
 * data type picks the control, and `enabledWhen` / `computed` decide whether the user may
 * type into it at all.
 */
export function FieldControl({ field, value, ctx, messages, culture, onChange, onBlur }: Props) {
  const label = localized(field.label, culture);
  const help = localized(field.help, culture);
  const disabled = field.computed !== undefined || !evaluateAsBool(field.enabledWhen, ctx);
  const options = useOptions(field, ctx, culture);

  const worst = messages.find((m) => m.severity === 'error')
    ?? messages.find((m) => m.severity === 'warning')
    ?? messages[0];

  const inputId = `f-${field.path.replace(/[[\].]/g, '-')}`;
  const tone = worst ? `field--${worst.severity}` : '';

  return (
    <div className={`field ${tone}`}>
      <label className="field__label" htmlFor={inputId}>
        {label}
        {field.symbol && <span className="field__symbol">{field.symbol}</span>}
        {(field.required || field.requiredWhen) && <span className="field__required" aria-hidden="true">*</span>}
      </label>

      {renderControl()}

      {help && <p className="field__help">{help}</p>}
      {field.b3Field && <p className="field__b3">B3: {field.b3Field}</p>}
      <MessageList messages={messages} />
    </div>
  );

  function renderControl() {
    switch (field.dataType) {
      case 'boolean':
        return (
          <select
            id={inputId}
            className="field__input"
            disabled={disabled}
            value={value === true ? 'true' : value === false ? 'false' : ''}
            onChange={(e) => onChange(e.target.value === '' ? null : e.target.value === 'true')}
            onBlur={onBlur}
          >
            <option value="">{localized({ pt: '— selecione —', en: '— choose —' }, culture)}</option>
            <option value="true">{localized({ pt: 'Sim', en: 'Yes' }, culture)}</option>
            <option value="false">{localized({ pt: 'Não', en: 'No' }, culture)}</option>
          </select>
        );

      case 'enum':
        return (
          <select
            id={inputId}
            className="field__input"
            disabled={disabled}
            value={typeof value === 'string' ? value : ''}
            onChange={(e) => onChange(e.target.value === '' ? null : e.target.value)}
            onBlur={onBlur}
          >
            <option value="">{localized({ pt: '— selecione —', en: '— choose —' }, culture)}</option>
            {options.map((option) => (
              <option key={option.code} value={option.code} title={option.help}>
                {option.label}
              </option>
            ))}
          </select>
        );

      case 'date':
        return (
          <input
            id={inputId}
            type="date"
            className="field__input"
            disabled={disabled}
            value={typeof value === 'string' ? value : ''}
            onChange={(e) => onChange(e.target.value === '' ? null : e.target.value)}
            onBlur={onBlur}
          />
        );

      case 'integer':
      case 'decimal':
      case 'percent':
      case 'money':
        return (
          <div className="field__numeric">
            <input
              id={inputId}
              type="number"
              inputMode="decimal"
              className="field__input"
              disabled={disabled}
              min={field.min}
              max={field.max}
              step={field.dataType === 'integer' ? 1 : stepFor(field.decimals)}
              value={typeof value === 'number' ? value : typeof value === 'string' ? value : ''}
              onChange={(e) => onChange(e.target.value === '' ? null : Number(e.target.value))}
              onBlur={onBlur}
            />
            <span className="field__unit">{field.dataType === 'percent' ? '%' : field.unit ?? ''}</span>
          </div>
        );

      case 'text':
        return (
          <textarea
            id={inputId}
            className="field__input field__input--text"
            disabled={disabled}
            rows={3}
            maxLength={field.maxLength}
            value={typeof value === 'string' ? value : ''}
            onChange={(e) => onChange(e.target.value === '' ? null : e.target.value)}
            onBlur={onBlur}
          />
        );

      default:
        return (
          <input
            id={inputId}
            type="text"
            className="field__input"
            list={options.length > 0 ? `${inputId}-options` : undefined}
            disabled={disabled}
            maxLength={field.maxLength}
            value={typeof value === 'string' ? value : value === null || value === undefined ? '' : String(value)}
            onChange={(e) => onChange(e.target.value === '' ? null : e.target.value)}
            onBlur={onBlur}
          />
        );
    }
  }
}

function stepFor(decimals: number | undefined): string {
  if (!decimals) return 'any';
  return (10 ** -Math.min(decimals, 8)).toFixed(Math.min(decimals, 8));
}

interface DisplayOption {
  code: string;
  label: string;
  help?: string;
}

/**
 * Options come either inline from the template or, for lists that change on their own cadence
 * (the underlying master), from the reference endpoint named by `optionSource`.
 */
function useOptions(field: TemplateField, ctx: EvalContext, culture: string): DisplayOption[] {
  const [remote, setRemote] = useState<ReferenceItem[]>([]);

  useEffect(() => {
    if (!field.optionSource) return;
    let cancelled = false;
    api.reference(field.optionSource)
      .then((items) => { if (!cancelled) setRemote(items); })
      .catch(() => { if (!cancelled) setRemote([]); });
    return () => { cancelled = true; };
  }, [field.optionSource]);

  if (field.options?.length) {
    return field.options
      .filter((option) => evaluateAsBool(option.visibleWhen, ctx))
      .map((option) => ({
        code: option.code,
        label: localized(option.label, culture),
        help: localized(option.help, culture) || undefined,
      }));
  }

  return remote.map((item) => ({ code: item.code, label: `${item.code} — ${item.name}`, help: item.group }));
}
