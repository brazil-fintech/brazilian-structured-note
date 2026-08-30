/* --------------------------------------------------------------------------
   Reference data used by the server-side checks that the browser cannot run:
   the national holiday calendar behind the business-day rules, and the
   underlying master behind the "underlyings" option source.

   Re-runnable: every insert is guarded.
   -------------------------------------------------------------------------- */

MERGE ref.Holiday AS target
USING (VALUES
    ('BRASIL', '2026-01-01', 'Confraternização Universal'),
    ('BRASIL', '2026-02-16', 'Carnaval'),
    ('BRASIL', '2026-02-17', 'Carnaval'),
    ('BRASIL', '2026-04-03', 'Sexta-feira Santa'),
    ('BRASIL', '2026-04-21', 'Tiradentes'),
    ('BRASIL', '2026-05-01', 'Dia do Trabalho'),
    ('BRASIL', '2026-06-04', 'Corpus Christi'),
    ('BRASIL', '2026-09-07', 'Independência'),
    ('BRASIL', '2026-10-12', 'Nossa Senhora Aparecida'),
    ('BRASIL', '2026-11-02', 'Finados'),
    ('BRASIL', '2026-11-15', 'Proclamação da República'),
    ('BRASIL', '2026-11-20', 'Consciência Negra'),
    ('BRASIL', '2026-12-25', 'Natal'),
    ('BRASIL', '2027-01-01', 'Confraternização Universal'),
    ('BRASIL', '2027-02-08', 'Carnaval'),
    ('BRASIL', '2027-02-09', 'Carnaval'),
    ('BRASIL', '2027-03-26', 'Sexta-feira Santa'),
    ('BRASIL', '2027-04-21', 'Tiradentes'),
    ('BRASIL', '2027-05-01', 'Dia do Trabalho'),
    ('BRASIL', '2027-05-27', 'Corpus Christi'),
    ('BRASIL', '2027-09-07', 'Independência'),
    ('BRASIL', '2027-10-12', 'Nossa Senhora Aparecida'),
    ('BRASIL', '2027-11-02', 'Finados'),
    ('BRASIL', '2027-11-15', 'Proclamação da República'),
    ('BRASIL', '2027-11-20', 'Consciência Negra'),
    ('BRASIL', '2027-12-25', 'Natal')
) AS source (CalendarCode, HolidayDate, Description)
    ON target.CalendarCode = source.CalendarCode AND target.HolidayDate = source.HolidayDate
WHEN NOT MATCHED THEN
    INSERT (CalendarCode, HolidayDate, Description)
    VALUES (source.CalendarCode, source.HolidayDate, source.Description);
GO

MERGE ref.Underlying AS target
USING (VALUES
    ('IBOV',      'Índice Bovespa',                'INDICES'),
    ('IBXX',      'Índice Brasil 100',             'INDICES'),
    ('SMLL',      'Índice Small Cap',              'INDICES'),
    ('PETR4',     'Petrobras PN',                  'ACOES'),
    ('VALE3',     'Vale ON',                       'ACOES'),
    ('ITUB4',     'Itaú Unibanco PN',              'ACOES'),
    ('BBAS3',     'Banco do Brasil ON',            'ACOES'),
    ('WEGE3',     'WEG ON',                        'ACOES'),
    ('USDBRL',    'Dólar dos EUA / Real',          'CAMBIO'),
    ('EURBRL',    'Euro / Real',                   'CAMBIO'),
    ('SPX',       'S&P 500',                       'INDICES_INT'),
    ('NDX',       'Nasdaq 100',                    'INDICES_INT'),
    ('SX5E',      'Euro Stoxx 50',                 'INDICES_INT'),
    ('AAPL',      'Apple Inc.',                    'ACOES_INT'),
    ('MSFT',      'Microsoft Corp.',               'ACOES_INT'),
    ('BRENT',     'Petróleo Brent',                'COMMODITIES'),
    ('OURO',      'Ouro spot',                     'COMMODITIES')
) AS source (Code, Name, AssetClass)
    ON target.Code = source.Code
WHEN NOT MATCHED THEN
    INSERT (Code, Name, AssetClass, IsActive)
    VALUES (source.Code, source.Name, source.AssetClass, 1);
GO
