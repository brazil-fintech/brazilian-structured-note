import type { ValidationMessage } from '../engine/types';

/**
 * Findings shown next to the attribute they are about. Warnings are deliberately not styled
 * as failures: they never block a save, they just ask the user to look.
 */
export function MessageList({ messages }: { messages: ValidationMessage[] }) {
  if (messages.length === 0) return null;

  return (
    <ul className="messages">
      {messages.map((message, index) => (
        <li key={`${message.ruleId ?? message.origin}-${index}`} className={`message message--${message.severity}`}>
          <span className="message__icon" aria-hidden="true">
            {message.severity === 'error' ? '!' : message.severity === 'warning' ? '△' : 'i'}
          </span>
          <span className="message__text">{message.message}</span>
          {message.origin === 'serverCheck' && <span className="message__badge" title="Checked by the API">API</span>}
        </li>
      ))}
    </ul>
  );
}

export function MessageSummary({ messages, culture }: { messages: ValidationMessage[]; culture: string }) {
  const errors = messages.filter((m) => m.severity === 'error');
  const warnings = messages.filter((m) => m.severity === 'warning');
  if (errors.length === 0 && warnings.length === 0) return null;

  const en = culture.toLowerCase().startsWith('en');

  return (
    <div className="summary">
      {errors.length > 0 && (
        <span className="summary__count summary__count--error">
          {errors.length} {en ? (errors.length === 1 ? 'error' : 'errors') : errors.length === 1 ? 'erro' : 'erros'}
        </span>
      )}
      {warnings.length > 0 && (
        <span className="summary__count summary__count--warning">
          {warnings.length} {en ? (warnings.length === 1 ? 'warning' : 'warnings') : warnings.length === 1 ? 'alerta' : 'alertas'}
        </span>
      )}
    </div>
  );
}
