import { useMemo, useState } from 'react';
import { visibleSections } from '../engine/instance';
import { localized, ui } from '../engine/texts';
import type { FigureTemplate, InstanceValues } from '../engine/types';
import { useAssetForm } from '../state/useAssetForm';
import { MessageSummary } from './Messages';
import { RepeatingSection, SectionFields } from './SectionFields';

interface Props {
  template: FigureTemplate;
  assetId?: string | null;
  initialValues?: InstanceValues;
  initialRowVersion?: string;
  culture: string;
  onCancel: () => void;
  onSaved: (assetId: string) => void;
}

/**
 * The booking screen. Its whole structure comes from the template: the common attributes are
 * pinned at the top, every other block is a tab, and which of them exist at all depends on the
 * figure. Nothing here knows what a call spread is.
 */
export function AssetForm({ template, assetId, initialValues, initialRowVersion, culture, onCancel, onSaved }: Props) {
  const form = useAssetForm({ template, assetId, initialValues, initialRowVersion, culture, onSaved });

  const sections = useMemo(() => visibleSections(template, form.values), [template, form.values]);
  const commonSections = sections.filter((s) => s.kind === 'common');
  const tabs = sections.filter((s) => s.kind === 'tab');

  const [activeTab, setActiveTab] = useState(tabs[0]?.key ?? '');
  const active = tabs.find((t) => t.key === activeTab) ?? tabs[0];

  const blocked = form.errorCount > 0;
  const warningsOnly = !blocked && form.warningCount > 0;

  return (
    <form
      className="form"
      onSubmit={(e) => { e.preventDefault(); void form.submit(false); }}
    >
      <header className="form__header">
        <div>
          <h2 className="form__title">{template.commercialName ?? template.figureName}</h2>
          <p className="form__subtitle">
            <span className="badge">{template.figureCode}</span>
            <span>{template.figureName}</span>
            <span className="form__version">v{template.version}</span>
          </p>
          {template.description && <p className="form__description">{localized(template.description, culture)}</p>}
        </div>
        <div className="form__actions">
          {form.checking && <span className="form__checking">{ui.checking(culture)}</span>}
          <MessageSummary messages={form.allMessages} culture={culture} />
          <button type="button" className="button button--ghost" onClick={onCancel}>
            {ui.cancel(culture)}
          </button>
          <button type="submit" className="button button--primary" disabled={form.saving || blocked}>
            {form.saving ? ui.saving(culture) : ui.save(culture)}
          </button>
          {warningsOnly && (
            <button
              type="button"
              className="button button--warning"
              disabled={form.saving}
              onClick={() => { void form.submit(true); }}
            >
              {ui.saveAnyway(culture)}
            </button>
          )}
        </div>
      </header>

      {form.conflict && <div className="banner banner--error">{form.conflict}</div>}
      {form.saveError && <div className="banner banner--error">{form.saveError}</div>}

      {/* Common attributes: always on screen, whatever the figure. */}
      {commonSections.map((section) => (
        <section className="panel" key={section.key}>
          <h3 className="panel__title">{localized(section.label, culture)}</h3>
          <SectionFields section={section} form={form} culture={culture} />
        </section>
      ))}

      {/* Everything else — payoff, basket, cash flows, barriers — as tabs. */}
      {tabs.length > 0 && (
        <section className="panel">
          <nav className="tabs" role="tablist">
            {tabs.map((tab) => {
              const messages = form.messagesInSection(tab.key);
              const errors = messages.filter((m) => m.severity === 'error').length;
              const warnings = messages.filter((m) => m.severity === 'warning').length;
              return (
                <button
                  key={tab.key}
                  type="button"
                  role="tab"
                  aria-selected={active?.key === tab.key}
                  className={`tab ${active?.key === tab.key ? 'tab--active' : ''}`}
                  onClick={() => setActiveTab(tab.key)}
                >
                  {localized(tab.label, culture)}
                  {errors > 0 && <span className="tab__dot tab__dot--error">{errors}</span>}
                  {errors === 0 && warnings > 0 && <span className="tab__dot tab__dot--warning">{warnings}</span>}
                </button>
              );
            })}
          </nav>

          {active && (
            <div className="tabpanel" role="tabpanel">
              {active.repeating
                ? <RepeatingSection section={active} form={form} culture={culture} />
                : <SectionFields section={active} form={form} culture={culture} />}
            </div>
          )}
        </section>
      )}
    </form>
  );
}
