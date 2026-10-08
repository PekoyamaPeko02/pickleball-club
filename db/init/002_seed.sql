-- Pickleball Club — seed data.
-- PLACEHOLDER VALUES: the club name, courts, hours and prices below are examples so the app has something to show.
-- Replace them with the real ones (later through the Admin settings pages) before going live.

INSERT INTO club_settings (name, timezone, booking_window_days, reschedule_notice_hours)
VALUES ('Pickleball Club', 'Asia/Bangkok', 14, 48);

INSERT INTO courts (id, name, indoor, sort_order) VALUES
    ('c0000000-0000-0000-0000-000000000001', 'Court 1', true,  1),
    ('c0000000-0000-0000-0000-000000000002', 'Court 2', true,  2),
    ('c0000000-0000-0000-0000-000000000003', 'Court 3', false, 3),
    ('c0000000-0000-0000-0000-000000000004', 'Court 4', false, 4);

-- Open 06:00–23:00 every day.
INSERT INTO operating_hours (day_of_week, open_hour, close_hour)
SELECT d, 6, 23 FROM generate_series(0, 6) AS d;

-- Club-wide prices per hour (THB).
INSERT INTO price_rules (court_id, day_type, start_hour, end_hour, price_per_hour, label) VALUES
    (NULL, 'weekday',  6, 16, 300, 'Off-peak'),
    (NULL, 'weekday', 16, 23, 400, 'Peak'),
    (NULL, 'holiday',  6, 23, 400, 'Weekend & holiday');

-- Indoor courts cost more in the evening (a Court rule beats the Club-wide rule).
INSERT INTO price_rules (court_id, day_type, start_hour, end_hour, price_per_hour, label) VALUES
    ('c0000000-0000-0000-0000-000000000001', 'weekday', 16, 23, 450, 'Indoor peak'),
    ('c0000000-0000-0000-0000-000000000002', 'weekday', 16, 23, 450, 'Indoor peak');
