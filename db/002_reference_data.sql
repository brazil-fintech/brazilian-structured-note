/* --------------------------------------------------------------------------
   The national holiday calendar behind the business-day rules.

   The underlying master is not seeded here: it is B3's own export, loaded from
   reference/b3/ativos-subjacentes.csv by the ingestion worker (003 defines the
   table). Hand-written stand-ins were wrong — 11 of 17 codes did not exist in
   B3's catalogue at all.

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
