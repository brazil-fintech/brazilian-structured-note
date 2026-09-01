import type { LocalizedText } from './types';

/** Mirror of `Coe.Core.Validation.ValidationTexts`, so both sides word a constraint the same way. */
const en = (culture: string) => culture.toLowerCase().startsWith('en');

export function localized(text: LocalizedText | undefined, culture: string): string {
  if (!text) return '';
  return en(culture) && text.en ? text.en : text.pt;
}

function format(template: string, ...args: (string | number)[]): string {
  return template.replace(/\{(\d+)\}/g, (_, index) => String(args[Number(index)] ?? ''));
}

export const texts = {
  required: (c: string, label: string) =>
    format(en(c) ? '{0} is required.' : '{0} é obrigatório.', label),
  notANumber: (c: string, label: string) =>
    format(en(c) ? '{0} must be a number.' : '{0} deve ser numérico.', label),
  notAnInteger: (c: string, label: string) =>
    format(en(c) ? '{0} must be a whole number.' : '{0} deve ser um número inteiro.', label),
  notADate: (c: string, label: string) =>
    format(en(c) ? '{0} must be a valid date.' : '{0} deve ser uma data válida.', label),
  notABoolean: (c: string, label: string) =>
    format(en(c) ? '{0} must be yes or no.' : '{0} deve ser Sim ou Não.', label),
  notAnOption: (c: string, label: string, code: string) =>
    format(en(c) ? "{0}: '{1}' is not an accepted option." : "{0}: '{1}' não é uma opção aceita.", label, code),
  min: (c: string, label: string, value: number) =>
    format(en(c) ? '{0} must be at least {1}.' : '{0} deve ser no mínimo {1}.', label, value),
  max: (c: string, label: string, value: number) =>
    format(en(c) ? '{0} must be at most {1}.' : '{0} deve ser no máximo {1}.', label, value),
  decimals: (c: string, label: string, value: number) =>
    format(en(c) ? '{0} is registered with {1} decimal places.' : '{0} é registrado com {1} casas decimais.', label, value),
  maxLength: (c: string, label: string, value: number) =>
    format(en(c) ? '{0} accepts at most {1} characters.' : '{0} aceita no máximo {1} caracteres.', label, value),
  minItems: (c: string, value: number) =>
    format(en(c) ? 'at least {0} row(s) required.' : 'informe ao menos {0} linha(s).', value),
  maxItems: (c: string, value: number) =>
    format(en(c) ? 'at most {0} row(s) allowed.' : 'no máximo {0} linha(s).', value),
};

export const ui = {
  assets: (c: string) => (en(c) ? 'Assets' : 'Ativos'),
  referenceDate: (c: string) => (en(c) ? 'Reference date' : 'Data de referência'),
  newAsset: (c: string) => (en(c) ? 'New asset' : 'Novo ativo'),
  edit: (c: string) => (en(c) ? 'Edit' : 'Editar'),
  search: (c: string) => (en(c) ? 'Search name, IF code or ISIN' : 'Buscar por nome, Código IF ou ISIN'),
  allFigures: (c: string) => (en(c) ? 'All figures' : 'Todas as figuras'),
  chooseFigure: (c: string) => (en(c) ? 'Choose a figure' : 'Escolha uma figura'),
  chooseFigureHelp: (c: string) =>
    en(c)
      ? 'The attributes below change with the figure. Common data stays at the top; each block gets its own tab.'
      : 'Os atributos abaixo mudam conforme a figura. Os dados gerais ficam no topo; cada bloco tem sua aba.',
  cancel: (c: string) => (en(c) ? 'Cancel' : 'Cancelar'),
  bookableOnly: (c: string) => (en(c) ? 'Only what can be booked' : 'Somente o que pode ser registrado'),
  figureCoverage: (c: string, bookable: number, published: number) =>
    en(c)
      ? `${bookable} of B3's ${published} figures can be booked here`
      : `${bookable} das ${published} figuras da B3 podem ser registradas aqui`,
  figureNotConfigured: (c: string) =>
    en(c)
      ? 'B3 publishes this figure, but its attributes are not modelled here yet, so there is no form to fill in.'
      : 'A B3 publica esta figura, mas seus atributos ainda não estão modelados aqui — não há formulário para preencher.',
  figureQuarantined: (c: string) =>
    en(c)
      ? 'The domain file for this figure does not compile; the ingestion log has the errors.'
      : 'O arquivo de domínio desta figura não compila; os erros estão no log de ingestão.',
  availability: (c: string, availability: string) => {
    const pt: Record<string, string> = {
      Available: 'Disponível',
      Pending: 'Não liberada',
      Quarantined: 'Em quarentena',
      Retired: 'Descontinuada',
      NotConfigured: 'Sem formulário'
    };
    const gb: Record<string, string> = {
      Available: 'Available',
      Pending: 'Not released',
      Quarantined: 'Quarantined',
      Retired: 'Retired',
      NotConfigured: 'No form'
    };
    return (en(c) ? gb : pt)[availability] ?? availability;
  },
  save: (c: string) => (en(c) ? 'Save' : 'Salvar'),
  saveAnyway: (c: string) => (en(c) ? 'Save with warnings' : 'Salvar com alertas'),
  saving: (c: string) => (en(c) ? 'Saving…' : 'Salvando…'),
  addRow: (c: string) => (en(c) ? 'Add row' : 'Adicionar linha'),
  removeRow: (c: string) => (en(c) ? 'Remove' : 'Remover'),
  noRows: (c: string) => (en(c) ? 'No rows yet.' : 'Nenhuma linha registrada.'),
  noAssets: (c: string) => (en(c) ? 'No asset is live on this date.' : 'Nenhum ativo vigente nesta data.'),
  loading: (c: string) => (en(c) ? 'Loading…' : 'Carregando…'),
  checking: (c: string) => (en(c) ? 'Checking…' : 'Verificando…'),
  errors: (c: string) => (en(c) ? 'errors' : 'erros'),
  warnings: (c: string) => (en(c) ? 'warnings' : 'alertas'),
  yes: (c: string) => (en(c) ? 'Yes' : 'Sim'),
  no: (c: string) => (en(c) ? 'No' : 'Não'),
  choose: (c: string) => (en(c) ? '— choose —' : '— selecione —'),
  savedAt: (c: string) => (en(c) ? 'Saved' : 'Salvo'),
  modality: (c: string) => (en(c) ? 'Modality' : 'Modalidade'),
  figure: (c: string) => (en(c) ? 'Figure' : 'Figura'),
  maturity: (c: string) => (en(c) ? 'Maturity' : 'Vencimento'),
  issue: (c: string) => (en(c) ? 'Issue' : 'Emissão'),
  notional: (c: string) => (en(c) ? 'Notional' : 'Valor de emissão'),
  status: (c: string) => (en(c) ? 'Status' : 'Situação'),
  underlying: (c: string) => (en(c) ? 'Underlying' : 'Ativo subjacente'),
  name: (c: string) => (en(c) ? 'Name' : 'Nome'),
};
