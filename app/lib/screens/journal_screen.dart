import 'package:flutter/material.dart';

import '../api/api_client.dart';
import '../api/models.dart';
import '../theme.dart';
import '../widgets/forecast_card.dart';
import '../widgets/rules_card.dart';
import 'add_catch_screen.dart';

class JournalScreen extends StatefulWidget {
  const JournalScreen({super.key});

  @override
  State<JournalScreen> createState() => _JournalScreenState();
}

class _JournalScreenState extends State<JournalScreen> {
  List<Spot> _spots = [];
  Spot? _spot;
  DateTime _date = DateTime.now();

  bool _loading = true;
  String? _error;

  Forecast? _forecast;
  ZoneRules? _rules;
  List<CatchItem> _catches = [];

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
      final spots = await ApiClient.instance.getSpots();
      if (!mounted) return;
      setState(() {
        _spots = spots;
        if (spots.isNotEmpty && _spot == null) _spot = spots.first;
      });
      await _loadDetails();
    } on Exception catch (e) {
      if (!mounted) return;
      setState(() => _error = e.toString());
    } finally {
      if (mounted) setState(() => _loading = false);
    }
  }

  Future<void> _loadDetails() async {
    final spot = _spot;
    if (spot == null) return;
    setState(() => _forecast = null);
    try {
      final results = await Future.wait([
        ApiClient.instance.getForecast(spot.id, _date),
        ApiClient.instance.getZoneRules(spot.zoneId),
        ApiClient.instance.getCatches(spot.id),
      ]);
      if (!mounted) return;
      setState(() {
        _forecast = results[0] as Forecast;
        _rules = results[1] as ZoneRules;
        _catches = (results[2] as List).cast<CatchItem>();
      });
    } on Exception {
      if (!mounted) return;
      // отдельные панели остаются на старом состоянии; ошибка видна в снакбаре
      ScaffoldMessenger.of(context).showSnackBar(
        const SnackBar(content: Text('Не удалось загрузить прогноз/правила')),
      );
    }
  }

  Future<void> _pickDate() async {
    final picked = await showDatePicker(
      context: context,
      initialDate: _date,
      firstDate: DateTime(2026, 1, 1),
      lastDate: DateTime(2026, 12, 31),
    );
    if (picked == null) return;
    setState(() => _date = picked);
    await _loadDetails();
  }

  Future<void> _openAddCatch() async {
    final created = await Navigator.of(context).push<bool>(
      MaterialPageRoute(
        builder: (_) => AddCatchScreen(
          spots: _spots,
          initialSpot: _spot,
          initialDate: _date,
        ),
      ),
    );
    if (created == true) await _loadDetails();
  }

  @override
  Widget build(BuildContext context) {
    return Scaffold(
      appBar: AppBar(
        title: const Text('KLEVO'),
        actions: [
          if (_spot != null)
            Padding(
              padding: const EdgeInsets.only(right: 16),
              child: IconButton(
                tooltip: 'Обновить',
                onPressed: _loadDetails,
                icon: const Icon(Icons.refresh),
              ),
            ),
        ],
      ),
      floatingActionButton: _spot == null
          ? null
          : FloatingActionButton.extended(
              onPressed: _openAddCatch,
              backgroundColor: AppColors.accent,
              foregroundColor: AppColors.canvas,
              icon: const Icon(Icons.add),
              label: const Text('УЛОВ'),
            ),
      body: _buildBody(),
    );
  }

  Widget _buildBody() {
    if (_loading && _spots.isEmpty) {
      return const Center(child: CircularProgressIndicator());
    }
    if (_error != null && _spots.isEmpty) {
      return Center(
        child: Padding(
          padding: const EdgeInsets.all(24),
          child: Column(
            mainAxisSize: MainAxisSize.min,
            children: [
              const Icon(Icons.cloud_off, size: 48, color: AppColors.textMuted),
              const SizedBox(height: 12),
              Text('Не удалось подключиться к API\n$_error',
                  textAlign: TextAlign.center,
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

    return RefreshIndicator(
      onRefresh: _loadDetails,
      child: ListView(
        padding: const EdgeInsets.fromLTRB(16, 8, 16, 88),
        children: [
          _spotSelector(),
          const SizedBox(height: 12),
          _dateRow(),
          const SizedBox(height: 12),
          if (_forecast != null) ...[
            ForecastCard(forecast: _forecast!),
            const SizedBox(height: 12),
          ],
          if (_rules != null) ...[
            RulesCard(rules: _rules!),
            const SizedBox(height: 12),
          ],
          _catchesHeader(),
          if (_catches.isEmpty)
            const Padding(
              padding: EdgeInsets.symmetric(vertical: 24),
              child: Text(
                'Уловов ещё нет. Добавь первый через кнопку «УЛОВ».',
                style: TextStyle(color: AppColors.textMuted),
              ),
            )
          else
            for (final c in _catches) _catchTile(c),
        ],
      ),
    );
  }

  Widget _spotSelector() {
    return DropdownButtonFormField<Spot>(
      initialValue: _spot,
      isExpanded: true,
      decoration: const InputDecoration(
        labelText: 'ТОЧКА',
        prefixIcon: Icon(Icons.place_outlined, size: 20),
      ),
      items: [
        for (final s in _spots)
          DropdownMenuItem(value: s, child: Text(s.name, overflow: TextOverflow.ellipsis)),
      ],
      onChanged: (s) {
        if (s == null) return;
        setState(() => _spot = s);
        _loadDetails();
      },
    );
  }

  Widget _dateRow() {
    return Card(
      child: InkWell(
        onTap: _pickDate,
        borderRadius: BorderRadius.circular(10),
        child: Padding(
          padding: const EdgeInsets.symmetric(horizontal: 16, vertical: 12),
          child: Row(
            children: [
              const Icon(Icons.calendar_today, size: 16, color: AppColors.accent),
              const SizedBox(width: 10),
              Text(
                '${_date.day.toString().padLeft(2, '0')}.${_date.month.toString().padLeft(2, '0')}.${_date.year}',
                style: const TextStyle(fontSize: 15, color: AppColors.textPrimary),
              ),
              const Spacer(),
              const Icon(Icons.chevron_right, size: 18, color: AppColors.textMuted),
            ],
          ),
        ),
      ),
    );
  }

  Widget _catchesHeader() {
    return Padding(
      padding: const EdgeInsets.only(top: 8, bottom: 8),
      child: Row(
        children: [
          Text(
            'УЛОВЫ · ${_spot?.name ?? ''}',
            style: Theme.of(context).textTheme.labelLarge?.copyWith(
                  color: AppColors.textSecondary,
                  fontSize: 11,
                ),
          ),
          const SizedBox(width: 8),
          Text(
            '${_catches.length}',
            style: const TextStyle(fontFamily: 'RobotoMono', color: AppColors.accent),
          ),
        ],
      ),
    );
  }

  Widget _catchTile(CatchItem c) {
    final size = '${c.lengthCm?.toStringAsFixed(0) ?? '—'} см';
    final weight = '${c.weightKg?.toStringAsFixed(2) ?? '—'} кг';
    return Card(
      child: Padding(
        padding: const EdgeInsets.all(12),
        child: Row(
          children: [
            if (c.photoUrl != null && c.photoUrl!.isNotEmpty)
              ClipRRect(
                borderRadius: BorderRadius.circular(8),
                child: Image.network(
                  '${ApiClient.instance.baseUrl}${c.photoUrl}',
                  width: 56,
                  height: 56,
                  fit: BoxFit.cover,
                  errorBuilder: (_, _, _) => Container(
                    width: 56,
                    height: 56,
                    color: AppColors.cardRaised,
                    child: const Icon(Icons.image_not_supported_outlined,
                        color: AppColors.textMuted),
                  ),
                ),
              )
            else
              Container(
                width: 56,
                height: 56,
                decoration: BoxDecoration(
                  color: AppColors.cardRaised,
                  borderRadius: BorderRadius.circular(8),
                ),
                child: const Icon(Icons.phishing_outlined, color: AppColors.textMuted),
              ),
            const SizedBox(width: 12),
            Expanded(
              child: Column(
                crossAxisAlignment: CrossAxisAlignment.start,
                children: [
                  Text(c.speciesName,
                      style: const TextStyle(fontSize: 15, fontWeight: FontWeight.w600)),
                  const SizedBox(height: 3),
                  Text(
                    '$size · $weight',
                    style: const TextStyle(
                      fontFamily: 'RobotoMono',
                      fontSize: 12,
                      color: AppColors.textSecondary,
                    ),
                  ),
                ],
              ),
            ),
            Text(
              c.dateLabel,
              style: const TextStyle(
                fontFamily: 'RobotoMono',
                fontSize: 11,
                color: AppColors.textMuted,
              ),
            ),
          ],
        ),
      ),
    );
  }
}
