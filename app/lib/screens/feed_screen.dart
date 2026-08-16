import 'package:flutter/material.dart';

import '../api/api_client.dart';
import '../api/models.dart';
import '../theme.dart';

class FeedScreen extends StatefulWidget {
  const FeedScreen({super.key});

  @override
  State<FeedScreen> createState() => _FeedScreenState();
}

class _FeedScreenState extends State<FeedScreen> {
  List<FeedItem> _items = [];
  bool _loading = true;
  bool _photoOnly = false;
  String? _error;

  @override
  void initState() {
    super.initState();
    _load();
  }

  Future<void> _load() async {
    setState(() {
      _loading = true;
      _error = null;
    });
    try {
      final items = await ApiClient.instance.getFeed(limit: 100);
      if (!mounted) return;
      setState(() => _items = items);
    } on Exception catch (e) {
      if (!mounted) return;
      setState(() => _error = e.toString());
    } finally {
      if (mounted) setState(() => _loading = false);
    }
  }

  @override
  Widget build(BuildContext context) {
    return Scaffold(
      appBar: AppBar(
        title: const Text('ЛЕНТА УЛОВОВ'),
        actions: [
          Padding(
            padding: const EdgeInsets.only(right: 16),
            child: IconButton(
              tooltip: 'Обновить',
              onPressed: _load,
              icon: const Icon(Icons.refresh),
            ),
          ),
        ],
      ),
      body: _buildBody(),
    );
  }

  Widget _buildBody() {
    if (_loading) return const Center(child: CircularProgressIndicator());
    if (_error != null) {
      return Center(
        child: Padding(
          padding: const EdgeInsets.all(24),
          child: Column(
            mainAxisSize: MainAxisSize.min,
            children: [
              const Icon(Icons.cloud_off, size: 48, color: AppColors.textMuted),
              const SizedBox(height: 12),
              Text('$_error', textAlign: TextAlign.center,
                  style: const TextStyle(color: AppColors.textSecondary)),
              const SizedBox(height: 16),
              FilledButton(
                onPressed: _load,
                style: FilledButton.styleFrom(backgroundColor: AppColors.accent),
                child: const Text('ПОВТОРИТЬ'),
              ),
            ],
          ),
        ),
      );
    }

    final visible = _photoOnly ? _items.where((i) => i.photoUrl != null).toList() : _items;

    return Column(
      children: [
        Padding(
          padding: const EdgeInsets.fromLTRB(16, 8, 16, 4),
          child: Row(
            children: [
              Expanded(
                child: Text(
                  '${visible.length} уловов',
                  style: const TextStyle(fontFamily: 'RobotoMono', fontSize: 11, color: AppColors.textMuted),
                ),
              ),
              ChoiceChip(
                label: const Text('ТОЛЬКО С ФОТО'),
                selected: _photoOnly,
                onSelected: (v) => setState(() => _photoOnly = v),
                selectedColor: AppColors.accentDim,
                labelStyle: TextStyle(
                  fontSize: 11,
                  fontWeight: FontWeight.w700,
                  letterSpacing: 0.5,
                  color: _photoOnly ? AppColors.accent : AppColors.textSecondary,
                ),
                side: BorderSide(color: _photoOnly ? AppColors.accent : AppColors.hairline),
                shape: RoundedRectangleBorder(borderRadius: BorderRadius.circular(1440)),
                showCheckmark: false,
              ),
            ],
          ),
        ),
        Expanded(
          child: visible.isEmpty
              ? const Center(
                  child: Text('Пока пусто',
                      style: TextStyle(color: AppColors.textMuted)),
                )
              : RefreshIndicator(
                  onRefresh: _load,
                  child: ListView.builder(
                    padding: const EdgeInsets.fromLTRB(16, 8, 16, 24),
                    itemCount: visible.length,
                    itemBuilder: (_, i) => _FeedCard(item: visible[i]),
                  ),
                ),
        ),
      ],
    );
  }
}

class _FeedCard extends StatelessWidget {
  const _FeedCard({required this.item});
  final FeedItem item;

  String get _dateLabel {
    final l = item.caughtAt.toLocal();
    return '${l.day.toString().padLeft(2, '0')}.${l.month.toString().padLeft(2, '0')}.${l.year}';
  }

  @override
  Widget build(BuildContext context) {
    final photo = item.photoUrl;
    final rule = item.rule;
    final hasRule = rule.zoneName.isNotEmpty;

    return Card(
      child: Padding(
        padding: const EdgeInsets.all(12),
        child: Column(
          crossAxisAlignment: CrossAxisAlignment.start,
          children: [
            Row(
              children: [
                Expanded(
                  child: Text(
                    item.speciesName,
                    style: const TextStyle(fontSize: 16, fontWeight: FontWeight.w600),
                  ),
                ),
                Text(
                  _dateLabel,
                  style: const TextStyle(fontFamily: 'RobotoMono', fontSize: 11, color: AppColors.textMuted),
                ),
              ],
            ),
            const SizedBox(height: 4),
            Text(
              '${item.spotName.isEmpty ? '—' : item.spotName} · ${item.waterType}',
              style: const TextStyle(
                fontFamily: 'RobotoMono',
                fontSize: 11,
                color: AppColors.textSecondary,
              ),
            ),
            if (photo != null && photo.isNotEmpty) ...[
              const SizedBox(height: 10),
              ClipRRect(
                borderRadius: BorderRadius.circular(8),
                child: AspectRatio(
                  aspectRatio: 16 / 9,
                  child: Image.network(
                    '${ApiClient.instance.baseUrl}$photo',
                    fit: BoxFit.cover,
                    errorBuilder: (_, _, _) => Container(
                      color: AppColors.cardRaised,
                      child: const Icon(Icons.broken_image_outlined,
                          color: AppColors.textMuted),
                    ),
                  ),
                ),
              ),
            ],
            const SizedBox(height: 10),
            Row(
              children: [
                Expanded(
                  child: Text(
                    '${item.lengthCm?.toStringAsFixed(0) ?? '—'} см · ${item.weightKg?.toStringAsFixed(2) ?? '—'} кг',
                    style: const TextStyle(
                      fontFamily: 'RobotoMono',
                      fontSize: 12,
                      color: AppColors.textSecondary,
                    ),
                  ),
                ),
                if (hasRule)
                  Container(
                    padding: const EdgeInsets.symmetric(horizontal: 10, vertical: 4),
                    decoration: BoxDecoration(
                      color: (rule.allowed ? AppColors.ok : AppColors.bad)
                          .withValues(alpha: 0.14),
                      border: Border.all(
                        color: (rule.allowed ? AppColors.ok : AppColors.bad)
                            .withValues(alpha: 0.5),
                      ),
                      borderRadius: BorderRadius.circular(1440),
                    ),
                    child: Text(
                      rule.allowed ? 'РАЗРЕШЕНО' : 'ЕСТЬ НАРУШЕНИЯ',
                      style: TextStyle(
                        color: rule.allowed ? AppColors.ok : AppColors.bad,
                        fontSize: 10,
                        fontWeight: FontWeight.w700,
                      ),
                    ),
                  ),
              ],
            ),
            if (hasRule && rule.summary.isNotEmpty) ...[
              const SizedBox(height: 8),
              Text(
                rule.summary,
                style: Theme.of(context).textTheme.bodySmall?.copyWith(fontSize: 12),
              ),
            ],
          ],
        ),
      ),
    );
  }
}
