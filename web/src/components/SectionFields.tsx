import { evaluateAsBool, type EvalContext } from '../engine/evaluate';
import { localized, ui } from '../engine/texts';
import type { Json, TemplateSection } from '../engine/types';
import type { AssetFormState } from '../state/useAssetForm';
import { readPath } from '../engine/paths';
import { FieldControl } from './FieldControl';

interface Props {
  section: TemplateSection;
  form: AssetFormState;
  culture: string;
}

/** A non-repeating block: the common header, or a tab such as payoff or barriers. */
export function SectionFields({ section, form, culture }: Props) {
  const ctx: EvalContext = { root: form.values };
  const fields = section.fields.filter((field) => evaluateAsBool(field.visibleWhen, ctx));

  return (
    <div className="section">
      {section.help && <p className="section__help">{localized(section.help, culture)}</p>}
      <div className="grid">
        {fields.map((field) => (
          <FieldControl
            key={field.path}
            field={field}
            ctx={ctx}
            culture={culture}
            value={readPath(form.values, field.path)}
            messages={form.messagesFor(field.path)}
            onChange={(value: Json) => form.setValue(field.path, value)}
            onBlur={() => form.touch(field.path)}
          />
        ))}
      </div>
    </div>
  );
}

/** A grid block: cash flows, basket components, observation dates. */
export function RepeatingSection({ section, form, culture }: Props) {
  const rows = Array.isArray(form.values[section.key]) ? (form.values[section.key] as Json[]) : [];
  const sectionMessages = form.messagesFor(section.key);
  const atMax = section.maxItems !== undefined && rows.length >= section.maxItems;

  return (
    <div className="section">
      {section.help && <p className="section__help">{localized(section.help, culture)}</p>}

      {rows.length === 0 && <p className="repeating__empty">{ui.noRows(culture)}</p>}

      {rows.map((row, index) => {
        const prefix = `${section.key}[${index}]`;
        const ctx: EvalContext = { root: form.values, item: row as Record<string, Json> };
        const columns = section.itemFields.filter((field) => evaluateAsBool(field.visibleWhen, ctx));

        return (
          <div className="repeating__row" key={prefix}>
            <div className="repeating__index">{index + 1}</div>
            <div className="grid grid--row">
              {columns.map((field) => {
                const path = `${prefix}.${field.key}`;
                return (
                  <FieldControl
                    key={path}
                    field={field}
                    ctx={ctx}
                    culture={culture}
                    value={readPath(form.values, path)}
                    messages={form.messagesFor(path)}
                    onChange={(value: Json) => form.setValue(path, value)}
                    onBlur={() => form.touch(path)}
                  />
                );
              })}
            </div>
            <button
              type="button"
              className="button button--ghost repeating__remove"
              onClick={() => form.removeRow(section, index)}
            >
              {ui.removeRow(culture)}
            </button>
          </div>
        );
      })}

      <div className="repeating__actions">
        <button type="button" className="button" disabled={atMax} onClick={() => form.addRow(section)}>
          + {ui.addRow(culture)}
        </button>
        {section.minItems !== undefined && (
          <span className="repeating__hint">
            {section.minItems}–{section.maxItems ?? '∞'}
          </span>
        )}
      </div>

      {sectionMessages.length > 0 && (
        <ul className="messages">
          {sectionMessages.map((message, index) => (
            <li key={index} className={`message message--${message.severity}`}>
              <span className="message__icon" aria-hidden="true">
                {message.severity === 'error' ? '!' : '△'}
              </span>
              <span className="message__text">{message.message}</span>
            </li>
          ))}
        </ul>
      )}
    </div>
  );
}
