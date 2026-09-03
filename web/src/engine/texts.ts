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

  // The published screen carries no API of its own, so "no connection" is a setting to fill in
  // rather than an outage to wait out. These word that, and the form that fixes it.
  apiUnreachable: (c: string, baseUrl: string) =>
    en(c)
      ? `Could not reach the API at ${baseUrl}.`
      : `Não foi possível falar com a API em ${baseUrl}.`,
  apiUnconfiguredTitle: (c: string) =>
    en(c)
      ? 'This page has not been pointed at an API yet.'
      : 'Esta página ainda não foi apontada para uma API.',
  apiUnconfigured: (c: string) =>
    en(c)
      ? 'This page is the booking screen only — it has no API of its own, and none has been configured for it yet.'
      : 'Esta página é apenas a tela de registro — ela não tem API própria, e nenhuma foi configurada para ela ainda.',
  apiUnreachableHelp: (c: string) =>
    en(c)
      ? 'Check that the API is running and that it allows calls from this address, then try again.'
      : 'Verifique se a API está no ar e se ela permite chamadas deste endereço, e tente novamente.',
  apiBaseUrlLabel: (c: string) => (en(c) ? 'API address' : 'Endereço da API'),
  apiConnect: (c: string) => (en(c) ? 'Connect' : 'Conectar'),
  apiRetry: (c: string) => (en(c) ? 'Try again' : 'Tentar novamente'),
  apiUseDefault: (c: string) => (en(c) ? 'Use the deployed default' : 'Usar o padrão da publicação'),

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

  // ----- B3 / CETIP upload files -----
  b3Files: (c: string) => (en(c) ? 'B3 files' : 'Arquivos B3'),
  b3FilesTitle: (c: string) => (en(c) ? 'CETIP upload files' : 'Arquivos de envio CETIP'),
  b3FilesHelp: (c: string) =>
    en(c)
      ? 'The registration as B3 receives it, written from the booked values: the Registro COE, plus the cash-flow, basket and fixing-date files these values call for (ENVIAR ARQUIVOS §4.8).'
      : 'O registro como a B3 o recebe, escrito a partir dos valores registrados: o Registro COE e os arquivos de fluxo de caixa, cesta e datas de fixing que esses valores exigem (ENVIAR ARQUIVOS §4.8).',
  participant: (c: string) => (en(c) ? 'Participant short name' : 'Nome simplificado do participante'),
  participantHelp: (c: string) =>
    en(c)
      ? 'Leave blank to use the one configured on the server. Every upload header carries it.'
      : 'Deixe em branco para usar o configurado no servidor. Todo cabeçalho de envio o carrega.',
  fileDate: (c: string) => (en(c) ? 'File date' : 'Data do arquivo'),
  generate: (c: string) => (en(c) ? 'Generate files' : 'Gerar arquivos'),
  generating: (c: string) => (en(c) ? 'Generating…' : 'Gerando…'),
  regenerate: (c: string) => (en(c) ? 'Generate again' : 'Gerar novamente'),
  download: (c: string) => (en(c) ? 'Download' : 'Baixar'),
  downloadAll: (c: string) => (en(c) ? 'Download all' : 'Baixar todos'),
  copyContent: (c: string) => (en(c) ? 'Copy' : 'Copiar'),
  copied: (c: string) => (en(c) ? 'Copied' : 'Copiado'),
  records: (c: string, count: number) =>
    en(c) ? `${count} record(s)` : `${count} registro(s)`,
  operation: (c: string) => (en(c) ? 'Operation' : 'Operação'),
  notes: (c: string) => (en(c) ? 'What went into the files' : 'O que entrou nos arquivos'),
  noFilesYet: (c: string) =>
    en(c)
      ? 'No file has been generated yet.'
      : 'Nenhum arquivo foi gerado ainda.',
  assetSaved: (c: string) =>
    en(c)
      ? 'Asset saved. Generate the B3 upload files below, or go back to the list.'
      : 'Ativo salvo. Gere abaixo os arquivos de envio à B3, ou volte para a lista.',
  backToList: (c: string) => (en(c) ? 'Back to the list' : 'Voltar para a lista'),
  saveFiles: (c: string) => (en(c) ? 'Generate and keep' : 'Gerar e arquivar'),
  savingFiles: (c: string) => (en(c) ? 'Keeping…' : 'Arquivando…'),
  filesSaved: (c: string) =>
    en(c)
      ? 'Files stored. They are kept exactly as they would be uploaded, and stay readable after the asset is edited.'
      : 'Arquivos armazenados. Ficam guardados exatamente como seriam enviados e permanecem legíveis após a edição do ativo.',
  savedSets: (c: string) => (en(c) ? 'Files kept for this certificate' : 'Arquivos guardados deste certificado'),
  noSavedSets: (c: string) =>
    en(c) ? 'Nothing has been kept for this certificate yet.' : 'Nada foi guardado deste certificado ainda.',
  generatedAt: (c: string) => (en(c) ? 'Generated' : 'Gerado em'),
  generatedBy: (c: string) => (en(c) ? 'by' : 'por'),
  bytes: (c: string, count: number) =>
    `${new Intl.NumberFormat(en(c) ? 'en-GB' : 'pt-BR').format(count)} bytes`,
};
