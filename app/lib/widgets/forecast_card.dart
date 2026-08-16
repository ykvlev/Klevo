import 'package:flutter/material.dart';

import '../api/models.dart';
import '../theme.dart';

class ForecastCard extends StatelessWidget {
  const ForecastCard({super.key, required this.forecast});

  final Forecast forecast;

  String get _scoreLabel {
    final s = forecast.score;
    if (s >= 70) return 'КЛЁВ ОТЛИЧНЫЙ';
    if (s >= 50) return 'КЛЁВ ХОРОШИЙ';
    if (s >= 30) return 'КЛЁВ СРЕДНИЙ';
    return 'КЛЁВ СЛАБЫЙ';
  }

  @override
  Widget build(BuildContext context) {
    final s = forecast.score;
    final accent = forecast.isMl ? AppColors.accent : AppColors.warn;

    return Card(
      child: Padding(
        padding: const EdgeInsets.all(16),
        child: Column(
          crossAxisAlignment: CrossAxisAlignment.start,
          children: [
            Row(
              children: [
                Expanded(
                  child: Text(
                    'ПРОГНОЗ КЛЁВА',
                    style: Theme.of(context).textTheme.labelLarge?.copyWith(
                          color: AppColors.textSecondary,
                          fontSize: 11,
                        ),
                  ),
                ),
                _Pill(
                  label: forecast.sourceLabel,
                  version: forecast.modelVersion,
                  color: accent,
                ),
              ],
            ),
            const SizedBox(height: 14),
            Row(
              crossAxisAlignment: CrossAxisAlignment.end,
              children: [
                Text(
                  '$s',
                  style: Theme.of(context).textTheme.displaySmall?.copyWith(
                        color: accent,
                        fontSize: 44,
                        height: 1,
                      ),
                ),
                const SizedBox(width: 8),
                Padding(
                  padding: const EdgeInsets.only(bottom: 4),
                  child: Text(
                    '/ 100  ·  $_scoreLabel',
                    style: Theme.of(context).textTheme.bodySmall,
                  ),
                ),
              ],
            ),
            const SizedBox(height: 10),
            ClipRRect(
              borderRadius: BorderRadius.circular(999),
              child: LinearProgressIndicator(
                value: s / 100,
                minHeight: 6,
                backgroundColor: AppColors.hairline,
                color: accent,
              ),
            ),
            const SizedBox(height: 14),
            Row(
              children: [
                _KeyValue(
                  label: 'ЛУЧШЕЕ ОКНО',
                  value: '${forecast.bestStart} – ${forecast.bestEnd}',
                ),
                const SizedBox(width: 24),
                _KeyValue(
                  label: 'ДОСТОВЕРНОСТЬ',
                  value: '${forecast.dataConfidence}%',
                ),
              ],
            ),
            if (forecast.dataNote.isNotEmpty) ...[
              const SizedBox(height: 12),
              Text(
                forecast.dataNote,
                style: Theme.of(context).textTheme.bodySmall?.copyWith(
                      fontSize: 11,
                      fontFamily: 'RobotoMono',
                      color: AppColors.textMuted,
                    ),
              ),
            ],
          ],
        ),
      ),
    );
  }
}

class _Pill extends StatelessWidget {
  const _Pill({required this.label, required this.version, required this.color});

  final String label;
  final String version;
  final Color color;

  @override
  Widget build(BuildContext context) {
    return Container(
      padding: const EdgeInsets.symmetric(horizontal: 10, vertical: 4),
      decoration: BoxDecoration(
        color: color.withValues(alpha: 0.14),
        border: Border.all(color: color.withValues(alpha: 0.5)),
        borderRadius: BorderRadius.circular(1440),
      ),
      child: Text(
        '$label · $version',
        style: TextStyle(
          color: color,
          fontSize: 11,
          fontWeight: FontWeight.w700,
          letterSpacing: 0.5,
        ),
      ),
    );
  }
}

class _KeyValue extends StatelessWidget {
  const _KeyValue({required this.label, required this.value});

  final String label;
  final String value;

  @override
  Widget build(BuildContext context) {
    return Column(
      crossAxisAlignment: CrossAxisAlignment.start,
      children: [
        Text(
          label,
          style: Theme.of(context)
              .textTheme
              .bodySmall
              ?.copyWith(fontSize: 10, letterSpacing: 0.8, color: AppColors.textMuted),
        ),
        const SizedBox(height: 3),
        Text(
          value,
          style: const TextStyle(
            fontSize: 14,
            fontWeight: FontWeight.w600,
            color: AppColors.textPrimary,
          ),
        ),
      ],
    );
  }
}
