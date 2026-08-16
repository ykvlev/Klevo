import 'package:flutter/material.dart';

import '../api/models.dart';
import '../theme.dart';

class RulesCard extends StatelessWidget {
  const RulesCard({super.key, required this.rules});

  final ZoneRules rules;

  @override
  Widget build(BuildContext context) {
    return Card(
      child: Padding(
        padding: const EdgeInsets.all(16),
        child: Column(
          crossAxisAlignment: CrossAxisAlignment.start,
          children: [
            Text(
              'ПРАВИЛА · ${rules.zoneName}',
              style: Theme.of(context).textTheme.labelLarge?.copyWith(
                    color: AppColors.textSecondary,
                    fontSize: 11,
                  ),
            ),
            if (rules.minSizes.isNotEmpty) ...[
              const SizedBox(height: 12),
              _Section('МИНИМАЛЬНЫЙ РАЗМЕР'),
              for (final m in rules.minSizes)
                _Line(
                  left: m.species,
                  right: '${_fmt(m.minSizeCm)} см',
                  mono: true,
                ),
            ],
            if (rules.dailyLimits.isNotEmpty) ...[
              const SizedBox(height: 12),
              _Section('СУТОЧНАЯ НОРМА'),
              for (final l in rules.dailyLimits)
                _Line(
                  left: l.species,
                  right: '${_fmt(l.value)} ${l.unit}',
                  mono: true,
                ),
            ],
            if (rules.defaultDailyLimitKg != null) ...[
              const SizedBox(height: 12),
              _Section('ОБЩАЯ НОРМА ВЫЛОВА'),
              _Line(
                left: 'все виды суммарно',
                right: '${_fmt(rules.defaultDailyLimitKg!)} кг',
                mono: true,
              ),
              if (rules.defaultLimitNote.isNotEmpty)
                Padding(
                  padding: const EdgeInsets.only(top: 4),
                  child: Text(
                    rules.defaultLimitNote,
                    style: Theme.of(context).textTheme.bodySmall?.copyWith(fontSize: 11),
                  ),
                ),
            ],
            if (rules.bans.isNotEmpty) ...[
              const SizedBox(height: 12),
              _Section('ЗАПРЕТЫ'),
              for (final b in rules.bans)
                _BanRow(ban: b),
            ],
          ],
        ),
      ),
    );
  }

  static String _fmt(double v) => v == v.roundToDouble() ? v.toStringAsFixed(0) : v.toStringAsFixed(1);
}

class _Section extends StatelessWidget {
  const _Section(this.label);
  final String label;

  @override
  Widget build(BuildContext context) {
    return Padding(
      padding: const EdgeInsets.only(bottom: 5),
      child: Text(
        label,
        style: Theme.of(context).textTheme.bodySmall?.copyWith(
              fontSize: 10,
              letterSpacing: 1.0,
              color: AppColors.accent,
              fontWeight: FontWeight.w700,
            ),
      ),
    );
  }
}

class _Line extends StatelessWidget {
  const _Line({required this.left, required this.right, this.mono = false});
  final String left;
  final String right;
  final bool mono;

  @override
  Widget build(BuildContext context) {
    final valueStyle = TextStyle(
      fontFamily: mono ? 'RobotoMono' : null,
      fontSize: 13,
      color: AppColors.textPrimary,
    );
    return Padding(
      padding: const EdgeInsets.symmetric(vertical: 2),
      child: Row(
        crossAxisAlignment: CrossAxisAlignment.baseline,
        textBaseline: TextBaseline.alphabetic,
        children: [
          Expanded(
            child: Text(
              left,
              style: Theme.of(context).textTheme.bodyMedium?.copyWith(
                    color: AppColors.textSecondary,
                    fontSize: 13,
                  ),
            ),
          ),
          Text(right, style: valueStyle),
        ],
      ),
    );
  }
}

class _BanRow extends StatelessWidget {
  const _BanRow({required this.ban});
  final Ban ban;

  @override
  Widget build(BuildContext context) {
    final target = ban.species.isEmpty ? 'все виды' : ban.species;
    return Padding(
      padding: const EdgeInsets.symmetric(vertical: 3),
      child: Row(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          Container(
            margin: const EdgeInsets.only(top: 2),
            padding: const EdgeInsets.symmetric(horizontal: 8, vertical: 2),
            decoration: BoxDecoration(
              color: AppColors.bad.withValues(alpha: 0.14),
              border: Border.all(color: AppColors.bad.withValues(alpha: 0.5)),
              borderRadius: BorderRadius.circular(1440),
            ),
            child: Text(
              ban.label,
              style: const TextStyle(
                color: AppColors.bad,
                fontSize: 10,
                fontWeight: FontWeight.w700,
              ),
            ),
          ),
          const SizedBox(width: 10),
          Expanded(
            child: Column(
              crossAxisAlignment: CrossAxisAlignment.start,
              children: [
                Text(
                  target,
                  style: const TextStyle(
                    fontSize: 13,
                    color: AppColors.textPrimary,
                  ),
                ),
                if (ban.ruleText.isNotEmpty)
                  Text(
                    ban.ruleText,
                    style: Theme.of(context).textTheme.bodySmall?.copyWith(fontSize: 11),
                  ),
              ],
            ),
          ),
        ],
      ),
    );
  }
}
